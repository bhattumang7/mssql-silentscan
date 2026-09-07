using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Catalog;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;

namespace SilentScan.Core.Predicates;

public static class AlwaysEncryptedAssignmentMismatchScanner
{
    private static readonly IReadOnlyDictionary<string, ResolvedRelation> EmptyResolvedViews = new Dictionary<string, ResolvedRelation>();

    public static IReadOnlyList<AlwaysEncryptedAssignmentMismatchFinding> Scan(SqlParseResult parseResult, DatabaseCatalog catalog)
    {
        var rule = CreateRule(parseResult.SourcePath, catalog);
        var walker = new ModuleWalker(parseResult.SourcePath, catalog, EmptyResolvedViews, rules: [rule]);
        parseResult.Fragment.Accept(walker);
        return Harvest(rule);
    }

    internal static Rule CreateRule(string sourcePath, DatabaseCatalog catalog) => new(sourcePath, catalog);

    internal static IReadOnlyList<AlwaysEncryptedAssignmentMismatchFinding> Harvest(Rule rule) =>
        [
            .. rule.Findings
                .OrderBy(f => f.SourcePath, StringComparer.Ordinal)
                .ThenBy(f => f.Line)
                .ThenBy(f => f.Column),
        ];

    internal sealed class Rule(string sourcePath, DatabaseCatalog catalog) : IModuleRule
    {
        public List<AlwaysEncryptedAssignmentMismatchFinding> Findings { get; } = [];

        private (CatalogTable Table, IList<ColumnReferenceExpression> Columns, QuerySpecification Source)? _pendingInsertSelect;

        public void OnEnterAssignmentSetClause(AssignmentSetClause node, ModuleWalker walker)
        {
            if (node.Column is not { } targetColumnRef)
            {
                return;
            }

            var scopeChain = walker.CurrentScopeChain();
            InspectAssignment(targetColumnRef, node.NewValue, scopeChain, node);
        }

        public void OnEnterInsertStatementScope(InsertStatement node, ModuleWalker walker)
        {
            var table = ResolveTarget(node.InsertSpecification.Target, node.WithCtesAndXmlNamespaces);
            if (table is not null)
            {
                InspectInsertColumns(table, node.InsertSpecification.Columns, node.InsertSpecification.InsertSource as ValuesInsertSource);
            }

            _pendingInsertSelect = table is not null && node.InsertSpecification.InsertSource is SelectInsertSource { Select: QuerySpecification querySpec }
                ? (table, node.InsertSpecification.Columns, querySpec)
                : null;
        }

        public void OnLeaveInsertStatementScope(InsertStatement node, ModuleWalker walker) => _pendingInsertSelect = null;

        public void OnEnterQuerySpecificationScope(QuerySpecification node, ScopeChain scopeChain, ModuleWalker walker)
        {
            if (_pendingInsertSelect is not { } pending || !ReferenceEquals(pending.Source, node))
            {
                return;
            }

            InspectInsertSelectColumns(pending.Table, pending.Columns, node, scopeChain);
        }

        public void OnEnterMergeStatementScope(MergeStatement node, ScopeChain scopeChain, ModuleWalker walker)
        {
            var table = ResolveTarget(node.MergeSpecification.Target, node.WithCtesAndXmlNamespaces);
            if (table is null)
            {
                return;
            }

            foreach (var actionClause in node.MergeSpecification.ActionClauses)
            {
                if (actionClause.Action is InsertMergeAction insert)
                {
                    InspectInsertColumns(table, insert.Columns, insert.Source);
                }
            }
        }

        private void InspectInsertColumns(CatalogTable table, IList<ColumnReferenceExpression> columns, ValuesInsertSource? values)
        {
            if (values is null || columns.Count == 0)
            {
                return;
            }

            for (var i = 0; i < columns.Count; i++)
            {
                var name = columns[i].MultiPartIdentifier.Identifiers[^1].Value;
                var column = table.FindColumn(name, catalog.IdentifierComparer);
                if (column is not { EncryptionType: not Catalog.ColumnEncryptionType.None })
                {
                    continue;
                }

                foreach (var columnValues in values.RowValues.Select(row => row.ColumnValues))
                {
                    if (i >= columnValues.Count || !IsNonNullLiteral(columnValues[i]))
                    {
                        continue;
                    }

                    Findings.Add(new AlwaysEncryptedAssignmentMismatchFinding(
                        AlwaysEncryptedAssignmentMismatchKind.LiteralSource,
                        table.QualifiedName,
                        column.Name,
                        column.EncryptionType.ToString(),
                        SourceTableQualifiedName: null,
                        SourceColumnName: null,
                        SourceEncryptionTypeDisplay: null,
                        sourcePath,
                        columnValues[i].StartLine,
                        columnValues[i].StartColumn));
                }
            }
        }

        private void InspectInsertSelectColumns(
            CatalogTable table,
            IList<ColumnReferenceExpression> columns,
            QuerySpecification source,
            ScopeChain scopeChain)
        {
            var selectExpressions = source.SelectElements.OfType<SelectScalarExpression>().ToList();
            if (columns.Count == 0 || selectExpressions.Count != columns.Count)
            {
                return;
            }

            for (var i = 0; i < columns.Count; i++)
            {
                var name = columns[i].MultiPartIdentifier.Identifiers[^1].Value;
                var targetColumn = table.FindColumn(name, catalog.IdentifierComparer);
                if (targetColumn is not { EncryptionType: not Catalog.ColumnEncryptionType.None })
                {
                    continue;
                }

                var sourceExpression = selectExpressions[i].Expression;

                if (BaseColumnResolver.ResolveBaseColumn(sourceExpression, sourcePath, scopeChain, catalog) is { } sourceRef)
                {
                    if (catalog.Find(sourceRef.TableQualifiedName)?.FindColumn(sourceRef.ColumnName, catalog.IdentifierComparer) is not { } sourceColumn
                        || !Catalog.AlwaysEncryptedCompatibility.IsMismatch(targetColumn, sourceColumn, catalog.IdentifierComparer))
                    {
                        continue;
                    }

                    Findings.Add(new AlwaysEncryptedAssignmentMismatchFinding(
                        AlwaysEncryptedAssignmentMismatchKind.EncryptionStateMismatch,
                        table.QualifiedName,
                        targetColumn.Name,
                        Catalog.AlwaysEncryptedCompatibility.FormatDisplay(targetColumn),
                        sourceRef.TableQualifiedName,
                        sourceRef.ColumnName,
                        Catalog.AlwaysEncryptedCompatibility.FormatDisplay(sourceColumn),
                        sourcePath,
                        sourceExpression.StartLine,
                        sourceExpression.StartColumn));
                    continue;
                }

                if (IsNonNullLiteral(sourceExpression))
                {
                    Findings.Add(new AlwaysEncryptedAssignmentMismatchFinding(
                        AlwaysEncryptedAssignmentMismatchKind.LiteralSource,
                        table.QualifiedName,
                        targetColumn.Name,
                        targetColumn.EncryptionType.ToString(),
                        SourceTableQualifiedName: null,
                        SourceColumnName: null,
                        SourceEncryptionTypeDisplay: null,
                        sourcePath,
                        sourceExpression.StartLine,
                        sourceExpression.StartColumn));
                }
            }
        }

        private void InspectAssignment(
            ColumnReferenceExpression targetColumnRef,
            ScalarExpression sourceExpression,
            ScopeChain scopeChain,
            TSqlFragment location)
        {
            if (BaseColumnResolver.ResolveBaseColumn(targetColumnRef, sourcePath, scopeChain, catalog) is not { } target
                || catalog.Find(target.TableQualifiedName)?.FindColumn(target.ColumnName, catalog.IdentifierComparer) is not { } targetColumn)
            {
                return;
            }

            if (BaseColumnResolver.ResolveBaseColumn(sourceExpression, sourcePath, scopeChain, catalog) is { } source)
            {
                if (catalog.Find(source.TableQualifiedName)?.FindColumn(source.ColumnName, catalog.IdentifierComparer) is not { } sourceColumn
                    || !Catalog.AlwaysEncryptedCompatibility.IsMismatch(targetColumn, sourceColumn, catalog.IdentifierComparer))
                {
                    return;
                }

                Findings.Add(new AlwaysEncryptedAssignmentMismatchFinding(
                    AlwaysEncryptedAssignmentMismatchKind.EncryptionStateMismatch,
                    target.TableQualifiedName,
                    target.ColumnName,
                    Catalog.AlwaysEncryptedCompatibility.FormatDisplay(targetColumn),
                    source.TableQualifiedName,
                    source.ColumnName,
                    Catalog.AlwaysEncryptedCompatibility.FormatDisplay(sourceColumn),
                    sourcePath,
                    location.StartLine,
                    location.StartColumn));
                return;
            }

            if (targetColumn.EncryptionType != Catalog.ColumnEncryptionType.None && IsNonNullLiteral(sourceExpression))
            {
                Findings.Add(new AlwaysEncryptedAssignmentMismatchFinding(
                    AlwaysEncryptedAssignmentMismatchKind.LiteralSource,
                    target.TableQualifiedName,
                    target.ColumnName,
                    targetColumn.EncryptionType.ToString(),
                    SourceTableQualifiedName: null,
                    SourceColumnName: null,
                    SourceEncryptionTypeDisplay: null,
                    sourcePath,
                    location.StartLine,
                    location.StartColumn));
            }
        }

        private static bool IsNonNullLiteral(ScalarExpression expression) =>
            expression is Literal and not NullLiteral and not DefaultLiteral;

        private CatalogTable? ResolveTarget(TableReference? target, WithCtesAndXmlNamespaces? withCtes)
        {
            var qualifiedName = DmlWriteTargetResolver.TryResolve(target, withCtes, catalog);
            return qualifiedName is null ? null : catalog.Find(qualifiedName);
        }
    }
}
