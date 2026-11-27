using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Catalog;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;

namespace SilentScan.Core.Predicates;

public static class IndexHintScanner
{
    private static readonly IReadOnlyDictionary<string, ResolvedRelation> EmptyResolvedViews = new Dictionary<string, ResolvedRelation>();

    public static IReadOnlyList<IndexHintFinding> Scan(SqlParseResult parseResult, DatabaseCatalog catalog)
    {
        var rule = CreateRule(parseResult.SourcePath, catalog);
        var walker = new ModuleWalker(parseResult.SourcePath, catalog, EmptyResolvedViews, rules: [rule]);
        parseResult.Fragment.Accept(walker);
    return Harvest(rule);
    }
    internal static Rule CreateRule(string sourcePath, DatabaseCatalog catalog) => new(sourcePath, catalog);

    internal static IReadOnlyList<IndexHintFinding> Harvest(Rule rule) =>
            [
            .. rule.Findings
                .OrderBy(f => f.SourcePath, StringComparer.Ordinal)
                .ThenBy(f => f.Line)
                .ThenBy(f => f.Column)
                .ThenBy(f => f.HintedIndexName, StringComparer.OrdinalIgnoreCase),
        ];


    internal sealed class Rule(string sourcePath, DatabaseCatalog catalog) : IModuleRule
    {
        public List<IndexHintFinding> Findings { get; } = [];

        public void OnEnterQuerySpecificationScope(QuerySpecification node, ScopeChain scopeChain, ModuleWalker walker) =>
            Inspect(node, node.FromClause, node.WhereClause?.SearchCondition, walker.CurrentCteRelations());

        public void OnEnterUpdateStatementScope(UpdateStatement node, ScopeChain scopeChain, ModuleWalker walker)
        {
            var spec = node.UpdateSpecification;
            var (byAlias, ordered) = FromScopeResolver.ResolveForDataModification(spec.Target, spec.FromClause, ResolutionContext(walker));
            InspectResolved(spec, byAlias, ordered, spec.FromClause, spec.WhereClause?.SearchCondition, spec.Target);
        }

        public void OnEnterDeleteStatementScope(DeleteStatement node, ScopeChain scopeChain, ModuleWalker walker)
        {
            var spec = node.DeleteSpecification;
            var (byAlias, ordered) = FromScopeResolver.ResolveForDataModification(spec.Target, spec.FromClause, ResolutionContext(walker));
            InspectResolved(spec, byAlias, ordered, spec.FromClause, spec.WhereClause?.SearchCondition, spec.Target);
        }

        private FromScopeResolver.ResolutionContext ResolutionContext(ModuleWalker walker) =>
            new(catalog, EmptyResolvedViews, sourcePath, Ledger: null, walker.CurrentCteRelations(), ProcScope: null);

        private void Inspect(TSqlFragment statement, FromClause? fromClause, BooleanExpression? whereCondition, IReadOnlyDictionary<string, ResolvedRelation> cteRelations)
        {
            if (fromClause is null)
            {
                return;
            }

            var (byAlias, ordered) = FromScopeResolver.Resolve(fromClause, catalog, EmptyResolvedViews, sourcePath, ledger: null, cteRelations, procScope: null);
            InspectResolved(statement, byAlias, ordered, fromClause, whereCondition, target: null);
        }

        private void InspectResolved(
            TSqlFragment statement,
            IReadOnlyDictionary<string, ScopeEntry> byAlias, IReadOnlyList<ScopeEntry> ordered,
            FromClause? fromClause, BooleanExpression? whereCondition, TableReference? target)
        {
            var scopeChain = new List<(IReadOnlyDictionary<string, ScopeEntry> ByAlias, IReadOnlyList<ScopeEntry> Ordered)> { (byAlias, ordered) };

            var joinNodes = fromClause is null ? [] : fromClause.TableReferences.SelectMany(PredicateTreeWalker.FlattenJoinNodes).ToList();

            var anyReferencedColumns = new HashSet<(string Table, string Column)>(TableColumnKeyComparer.For(catalog));
            var referenceVisitor = new BaseColumnResolver.ColumnReferenceCollector(sourcePath, scopeChain, anyReferencedColumns, catalog);
            var seekableColumns = new HashSet<(string Table, string Column)>(TableColumnKeyComparer.For(catalog));
            var seekableVisitor = new SeekableReferenceVisitor(
                new BaseColumnResolver.ColumnReferenceCollector(sourcePath, scopeChain, seekableColumns, catalog));
            foreach (var condition in joinNodes.Select(join => join.SearchCondition)
                .Append(whereCondition)
                .OfType<BooleanExpression>())
            {
                condition.Accept(referenceVisitor);
                condition.Accept(seekableVisitor);
            }

            var statementColumns = new HashSet<(string Table, string Column)>(TableColumnKeyComparer.For(catalog));
            statement.Accept(new BaseColumnResolver.ColumnReferenceCollector(sourcePath, scopeChain, statementColumns, catalog));
            var wildcardDetector = new WildcardDetector();
            statement.Accept(wildcardDetector);

            var namedTables = (fromClause is null ? [] : fromClause.TableReferences.SelectMany(PredicateTreeWalker.FlattenNamedTables))
                .Concat(target is NamedTableReference targetNamed ? [targetNamed] : [])
                .ToList();

            foreach (var namedTable in namedTables)
            {
                InspectNamedTable(namedTable, byAlias, new References(anyReferencedColumns, seekableColumns), statementColumns, wildcardDetector.Found);
            }
        }

        private sealed record References(
            HashSet<(string Table, string Column)> Any, HashSet<(string Table, string Column)> Seekable);

        private void InspectNamedTable(
            NamedTableReference namedTable, IReadOnlyDictionary<string, ScopeEntry> byAlias,
            References references, HashSet<(string Table, string Column)> statementColumns, bool hasWildcard)
        {
            var indexHints = namedTable.TableHints.OfType<IndexTableHint>().ToList();
            if (indexHints.Count == 0)
            {
                return;
            }

            var alias = namedTable.Alias?.Value ?? namedTable.SchemaObject.BaseIdentifier.Value;
            if (!byAlias.TryGetValue(alias, out var entry) || entry.IsViewLayer || entry.Relation.QualifiedName is not { } qualifiedName)
            {
                return;
            }

            var table = catalog.Find(qualifiedName);
            if (table is null || table.Kind != CatalogTableKind.Table)
            {
                return;
            }

            foreach (var hint in indexHints)
            {
                InspectHint(hint, table, references, statementColumns, hasWildcard);
            }
        }

        private void InspectHint(
            IndexTableHint hint, CatalogTable table,
            References references, HashSet<(string Table, string Column)> statementColumns, bool hasWildcard)
        {
            foreach (var indexValue in hint.IndexValues)
            {

                var hintedName = indexValue.Identifier?.Value;
                if (hintedName is null)
                {
                    continue;
                }

                var matchedIndex = table.Indexes.FirstOrDefault(i => catalog.IdentifierComparer.Equals(i.Name, hintedName));
                if (matchedIndex is null)
                {
                    continue;
                }

                if (matchedIndex.KeyColumns.Count == 0)
                {
                    continue;
                }

                var leadingColumn = matchedIndex.KeyColumns[0];
                if (!references.Seekable.Contains((table.QualifiedName, leadingColumn))
                    && (references.Any.Contains((table.QualifiedName, leadingColumn))
                        ? table.FindColumn(leadingColumn, catalog.IdentifierComparer) is not { IsComputed: true }
                        : !IsCoveringScanWithNoPredicate(matchedIndex, table, references.Any, statementColumns, hasWildcard)))
                {
                    Findings.Add(new IndexHintFinding(
                        IndexHintFindingKind.HintedIndexNotSeekable, table.QualifiedName, hintedName, leadingColumn,
                        sourcePath, hint.StartLine, hint.StartColumn));
                }
            }
        }


        private bool IsCoveringScanWithNoPredicate(
            CatalogIndex index, CatalogTable table,
            HashSet<(string Table, string Column)> anyReferencedColumns, HashSet<(string Table, string Column)> statementColumns, bool hasWildcard)
        {
            var comparer = catalog.IdentifierComparer;
            if (hasWildcard || anyReferencedColumns.Any(c => comparer.Equals(c.Table, table.QualifiedName)))
            {
                return false;
            }

            var covered = index.KeyColumns
                .Concat(index.IncludedColumns)
                .Concat(table.Indexes.Where(i => i.IsClustered && !i.IsColumnstore).SelectMany(i => i.KeyColumns))
                .ToHashSet(comparer);

            return statementColumns
                .Where(c => comparer.Equals(c.Table, table.QualifiedName))
                .All(c => covered.Contains(c.Column));
        }

        private sealed class WildcardDetector : TSqlFragmentVisitor
        {
            public bool Found { get; private set; }

            public override void ExplicitVisit(SelectStarExpression node) => Found = true;
        }
    }
}
