using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Catalog;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;
using SilentScan.Core.Common;
using SilentScan.Core.TypeInference;

namespace SilentScan.Core.Predicates;

public static class SelfReferencingDmlScanner
{
    private static readonly IReadOnlyDictionary<string, ResolvedRelation> EmptyResolvedViews = new Dictionary<string, ResolvedRelation>();

    public static IReadOnlyList<SelfReferencingDmlFinding> Scan(
        SqlParseResult parseResult, DatabaseCatalog catalog, IReadOnlyDictionary<string, ViewExpansionOrigin> viewExpansionMap)
    {
        var rule = CreateRule(parseResult.SourcePath, catalog, viewExpansionMap);
        var walker = new ModuleWalker(parseResult.SourcePath, catalog, EmptyResolvedViews, rules: [rule]);
        parseResult.Fragment.Accept(walker);
        return Harvest(rule);
    }

    internal static Rule CreateRule(string sourcePath, DatabaseCatalog catalog, IReadOnlyDictionary<string, ViewExpansionOrigin> viewExpansionMap) =>
        new(sourcePath, catalog, viewExpansionMap);

    internal static IReadOnlyList<SelfReferencingDmlFinding> Harvest(Rule rule) =>
        [
            .. rule.Findings
                .OrderBy(f => f.SourcePath, StringComparer.Ordinal)
                .ThenBy(f => f.Line)
                .ThenBy(f => f.Column),
        ];

    internal sealed class Rule(string sourcePath, DatabaseCatalog catalog, IReadOnlyDictionary<string, ViewExpansionOrigin> viewExpansionMap) : IModuleRule
    {
        public List<SelfReferencingDmlFinding> Findings { get; } = [];

        private readonly Dictionary<string, SqlType?> _variableTypes = new(StringComparer.OrdinalIgnoreCase);

        public void OnEnterTSqlBatch(TSqlBatch node, ModuleWalker walker) =>
            _variableTypes.Clear();

        public void OnEnterProcedureOrFunctionBody(ProcedureStatementBodyBase node, ModuleWalker walker)
        {
            _variableTypes.Clear();
            foreach (var parameter in node.Parameters)
            {
                _variableTypes[parameter.VariableName.Value] =
                    SqlTypeReferenceResolver.Resolve(parameter.DataType, columnCollation: null, catalog.TypeAliases);
            }
        }

        public void OnEnterDeclareVariableStatement(DeclareVariableStatement node, ModuleWalker walker)
        {
            foreach (var declaration in node.Declarations)
            {
                _variableTypes[declaration.VariableName.Value] =
                    SqlTypeReferenceResolver.Resolve(declaration.DataType, columnCollation: null, catalog.TypeAliases);
            }
        }

        private SqlType? BoundType(ScalarExpression expression) =>
            ScalarExpressionResolver.ResolveScalarType(
                expression, [], sourcePath,
                new ScalarExpressionResolver.ScalarTypeContext(null, catalog.TypeAliases, catalog, _variableTypes));

        public void OnEnterInsertStatementScope(InsertStatement node, ModuleWalker walker)
        {
            var spec = node.InsertSpecification;
            if (spec.Target is NamedTableReference targetRef && !TopPercentLiteralHelper.ReturnsAtMostOneRow(spec.TopRowFilter))
            {
                var targetQualifiedName = catalog.ResolveSynonymName(SchemaObjectNameHelper.Qualify(targetRef.SchemaObject));
                var cteNames = CteNamesOf(node.WithCtesAndXmlNamespaces);

                var match = spec.InsertSource is SelectInsertSource select
                    && !VariableWriteSites.ProducesAtMostOneRow(select.Select)
                    && !IsTopOne(select.Select)
                    && !IsPinnedToOneRowByUniqueKeys(select.Select, cteNames)
                    ? FindMatchInFragment(select.Select, targetQualifiedName, cteNames)
                    : null;
                Report(match, "INSERT", targetQualifiedName, node);
            }
        }

        public void OnEnterUpdateStatementScope(UpdateStatement node, ScopeChain scopeChain, ModuleWalker walker)
        {
            var spec = node.UpdateSpecification;
            var updateTargetAlias = (spec.Target as NamedTableReference)?.Alias?.Value ?? (spec.Target as NamedTableReference)?.SchemaObject.BaseIdentifier.Value;
            if (!TopPercentLiteralHelper.ReturnsAtMostOneRow(spec.TopRowFilter)
                && ResolveTargetQualifiedName(spec.Target, updateTargetAlias, scopeChain) is { } targetQualifiedName)
            {
                var cteNames = CteNamesOf(node.WithCtesAndXmlNamespaces);
                var match = IsTargetPinnedToOneRowByUniqueKey(spec.FromClause, spec.Target, spec.WhereClause, cteNames)
                    ? null
                    : FindMatchInFromClauseExtras(spec.FromClause, spec.Target, targetQualifiedName, cteNames)
                        ?? FindMatchInFragment(spec.WhereClause, targetQualifiedName, cteNames)
                        ?? spec.SetClauses.OfType<AssignmentSetClause>()
                            .Select(sc => FindMatchInFragment(sc.NewValue, targetQualifiedName, cteNames))
                            .FirstOrDefault(m => m is not null);
                Report(match, "UPDATE", targetQualifiedName, node);
            }
        }

        public void OnEnterDeleteStatementScope(DeleteStatement node, ScopeChain scopeChain, ModuleWalker walker)
        {
            var spec = node.DeleteSpecification;
            var deleteTargetAlias = (spec.Target as NamedTableReference)?.Alias?.Value ?? (spec.Target as NamedTableReference)?.SchemaObject.BaseIdentifier.Value;
            if (!TopPercentLiteralHelper.ReturnsAtMostOneRow(spec.TopRowFilter)
                && ResolveTargetQualifiedName(spec.Target, deleteTargetAlias, scopeChain) is { } targetQualifiedName)
            {
                var cteNames = CteNamesOf(node.WithCtesAndXmlNamespaces);
                var match = IsTargetPinnedToOneRowByUniqueKey(spec.FromClause, spec.Target, spec.WhereClause, cteNames)
                    ? null
                    : FindMatchInFromClauseExtras(spec.FromClause, spec.Target, targetQualifiedName, cteNames)
                        ?? FindMatchInFragment(spec.WhereClause, targetQualifiedName, cteNames);
                Report(match, "DELETE", targetQualifiedName, node);
            }
        }

        public void OnEnterMergeStatementScope(MergeStatement node, ScopeChain scopeChain, ModuleWalker walker)
        {
            var spec = node.MergeSpecification;
            var mergeTargetAlias = spec.TableAlias?.Value ?? (spec.Target as NamedTableReference)?.SchemaObject.BaseIdentifier.Value;
            if (!TopPercentLiteralHelper.ReturnsAtMostOneRow(spec.TopRowFilter)
                && ResolveTargetQualifiedName(spec.Target, mergeTargetAlias, scopeChain) is { } targetQualifiedName)
            {
                var cteNames = CteNamesOf(node.WithCtesAndXmlNamespaces);
                var match = FindMatchInFragment(spec.TableReference, targetQualifiedName, cteNames)
                    ?? FindMatchInFragment(spec.SearchCondition, targetQualifiedName, cteNames)
                    ?? spec.ActionClauses
                        .Select(actionClause => FindMatchInFragment(actionClause, targetQualifiedName, cteNames))
                        .FirstOrDefault(m => m is not null);
                Report(match, "MERGE", targetQualifiedName, node);
            }
        }

        private bool IsTargetPinnedToOneRowByUniqueKey(FromClause? fromClause, TableReference target, WhereClause? whereClause, HashSet<string> cteNames) =>
            target is NamedTableReference named
            && (fromClause is null
                ? SingleRowKeyPinning.PinsAtMostOneRow([named], PinningConjunct.From(whereClause?.SearchCondition), cteNames, catalog)
                : fromClause.TableReferences is [var only] && PinsEveryTable(only, SingleRowKeyPinning.AliasOf(named), whereClause, cteNames));

        private bool PinsEveryTable(TableReference reference, string? requiredAlias, WhereClause? whereClause, HashSet<string> cteNames)
        {
            var tables = new List<NamedTableReference>();
            var conjuncts = PinningConjunct.From(whereClause?.SearchCondition);
            return SingleRowKeyPinning.CollectPinnableTables(reference, tables, conjuncts, catalog.IdentifierComparer)
                && (requiredAlias is null
                    || tables.Exists(table => catalog.IdentifierComparer.Equals(SingleRowKeyPinning.AliasOf(table), requiredAlias)))
                && SingleRowKeyPinning.PinsAtMostOneRow(tables, conjuncts, cteNames, catalog);
        }

        private static QueryExpression Unparenthesized(QueryExpression query)
        {
            while (query is QueryParenthesisExpression parenthesized)
            {
                query = parenthesized.QueryExpression;
            }

            return query;
        }

        private static bool IsTopOne(QueryExpression query) =>
            Unparenthesized(query) is QuerySpecification spec && TopPercentLiteralHelper.ReturnsAtMostOneRow(spec.TopRowFilter);

        private bool IsPinnedToOneRowByUniqueKeys(QueryExpression query, HashSet<string> cteNames)
        {
            return Unparenthesized(query) is QuerySpecification { FromClause.TableReferences: [var only] } spec
                && PinsEveryTable(only, requiredAlias: null, spec.WhereClause, cteNames);
        }

        private HashSet<string> CteNamesOf(WithCtesAndXmlNamespaces? withClause) =>
            CteNameHelper.Names(withClause, catalog.IdentifierComparer);

        private void Report(SelfReferencingDmlFinding? match, string statementKind, string targetQualifiedName, TSqlFragment locationNode)
        {
            if (match is { } finding && catalog.Find(targetQualifiedName)?.IsMemoryOptimized != true)
            {
                Findings.Add(finding with { StatementKind = statementKind, TargetTableQualifiedName = targetQualifiedName, Line = locationNode.StartLine, Column = locationNode.StartColumn });
            }
        }

        private static string? ResolveTargetQualifiedName(TableReference target, string? alias, ScopeChain scopeChain)
        {
            if (target is not NamedTableReference || alias is null || scopeChain.Count == 0)
            {
                return null;
            }

            return scopeChain[0].ByAlias.TryGetValue(alias, out var entry) ? entry.Relation.QualifiedName : null;
        }

        private SelfReferencingDmlFinding? FindMatchInFromClauseExtras(FromClause? fromClause, TableReference target, string targetQualifiedName, HashSet<string> cteNames)
        {
            if (fromClause is null)
            {
                return null;
            }

            var targetAlias = target is NamedTableReference named ? named.Alias?.Value ?? named.SchemaObject.BaseIdentifier.Value : null;

            var collector = new NamedTableReferenceCollector();
            fromClause.Accept(collector);

            var skippedTargetEntry = false;
            foreach (var reference in collector.References)
            {
                var alias = reference.Alias?.Value ?? reference.SchemaObject.BaseIdentifier.Value;
                if (!skippedTargetEntry && catalog.IdentifierComparer.Equals(alias, targetAlias))
                {
                    skippedTargetEntry = true;
                    continue;
                }

                if (TryClassify(reference, targetQualifiedName, cteNames) is { } finding)
                {
                    return finding;
                }
            }

            return null;
        }

        private SelfReferencingDmlFinding? FindMatchInFragment(TSqlFragment? fragment, string targetQualifiedName, HashSet<string> cteNames)
        {
            if (fragment is null)
            {
                return null;
            }

            var collector = new NamedTableReferenceCollector();
            fragment.Accept(collector);

            return collector.References
                .Select(reference => TryClassify(reference, targetQualifiedName, cteNames))
                .FirstOrDefault(finding => finding is not null);
        }

        private SelfReferencingDmlFinding? TryClassify(NamedTableReference reference, string targetQualifiedName, HashSet<string> cteNames)
        {
            if (reference.SchemaObject.SchemaIdentifier is null && cteNames.Contains(reference.SchemaObject.BaseIdentifier.Value))
            {
                return null;
            }

            var qualifiedName = catalog.ResolveSynonymName(SchemaObjectNameHelper.Qualify(reference.SchemaObject));

            if (catalog.IdentifierComparer.Equals(qualifiedName, targetQualifiedName))
            {
                return new SelfReferencingDmlFinding(
                    SelfReferencingDmlFindingKind.DirectTableReference, StatementKind: "", targetQualifiedName, qualifiedName,
                    sourcePath, Line: 0, Column: 0);
            }

            if (catalog.Find(qualifiedName) is null
                && viewExpansionMap.TryGetValue(qualifiedName, out var origin)
                && origin.BaseTables.Contains(targetQualifiedName))
            {
                return new SelfReferencingDmlFinding(
                    SelfReferencingDmlFindingKind.ThroughView, StatementKind: "", targetQualifiedName, qualifiedName,
                    sourcePath, Line: 0, Column: 0);
            }

            return null;
        }

        private sealed class NamedTableReferenceCollector : TSqlFragmentVisitor
        {
            public List<NamedTableReference> References { get; } = [];

            public override void ExplicitVisit(NamedTableReference node)
            {
                References.Add(node);
                base.ExplicitVisit(node);
            }
        }
    }
}
