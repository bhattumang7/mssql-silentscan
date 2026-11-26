using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Catalog;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates.Normalization;

namespace SilentScan.Core.Predicates;

public static class PartialCompositeForeignKeyJoinScanner
{
    private static readonly IReadOnlyDictionary<string, ResolvedRelation> EmptyResolvedViews = new Dictionary<string, ResolvedRelation>();

    public sealed record CompositeForeignKey(
        string ConstraintName,
        string ParentTableQualifiedName,
        string ReferencedTableQualifiedName,
        IReadOnlyList<ForeignKeyColumnPair> Pairs);

    public static IReadOnlyList<CompositeForeignKey> BuildCompositeForeignKeys(DatabaseCatalog catalog) =>
        [.. catalog.ForeignKeys
            .GroupBy(fk => fk.ConstraintName, catalog.IdentifierComparer)
            .Where(g => g.Count() >= 2)
            .Select(g => new CompositeForeignKey(
                g.Key,
                g.First().ParentTableQualifiedName,
                g.First().ReferencedTableQualifiedName,
                [.. g.Select(fk => new ForeignKeyColumnPair(fk.ParentColumnName, fk.ReferencedColumnName))]))];

    public static IReadOnlyList<PartialCompositeForeignKeyJoinFinding> Scan(
        SqlParseResult parseResult, DatabaseCatalog catalog, IReadOnlyList<CompositeForeignKey> compositeForeignKeys)
    {
        if (compositeForeignKeys.Count == 0)
        {
            return [];
        }

        var rule = CreateRule(parseResult.SourcePath, catalog, compositeForeignKeys);
        var walker = new ModuleWalker(parseResult.SourcePath, catalog, EmptyResolvedViews, rules: [rule]);
        parseResult.Fragment.Accept(walker);
        return Harvest(rule);
    }

    internal static Rule CreateRule(string sourcePath, DatabaseCatalog catalog, IReadOnlyList<CompositeForeignKey> compositeForeignKeys) =>
        new(sourcePath, catalog, compositeForeignKeys);

    internal static IReadOnlyList<PartialCompositeForeignKeyJoinFinding> Harvest(Rule rule) =>
        [
            .. rule.Findings
                .OrderBy(f => f.SourcePath, StringComparer.Ordinal)
                .ThenBy(f => f.Line)
                .ThenBy(f => f.Column),
        ];

    internal sealed class Rule(string sourcePath, DatabaseCatalog catalog, IReadOnlyList<CompositeForeignKey> compositeForeignKeys) : IModuleRule
    {
        public List<PartialCompositeForeignKeyJoinFinding> Findings { get; } = [];

        public void OnEnterQuerySpecificationScope(QuerySpecification node, ScopeChain scopeChain, ModuleWalker walker) =>
            InspectFromClause(node.FromClause, node.WhereClause, walker.CurrentCteRelations(), walker);

        public void OnEnterUpdateStatementScope(UpdateStatement node, ScopeChain scopeChain, ModuleWalker walker) =>
            InspectFromClause(node.UpdateSpecification.FromClause, node.UpdateSpecification.WhereClause, walker.CurrentCteRelations(), walker);

        public void OnEnterDeleteStatementScope(DeleteStatement node, ScopeChain scopeChain, ModuleWalker walker) =>
            InspectFromClause(node.DeleteSpecification.FromClause, node.DeleteSpecification.WhereClause, walker.CurrentCteRelations(), walker);

        private void InspectFromClause(FromClause? fromClause, WhereClause? whereClause, IReadOnlyDictionary<string, ResolvedRelation> cteRelations, ModuleWalker walker)
        {
            if (fromClause is null)
            {
                return;
            }

            var (byAlias, ordered) = FromScopeResolver.Resolve(fromClause, catalog, EmptyResolvedViews, sourcePath, ledger: null, cteRelations, procScope: null);
            if (byAlias.Count == 0)
            {
                return;
            }

            var scopeChain = new List<(IReadOnlyDictionary<string, ScopeEntry> ByAlias, IReadOnlyList<ScopeEntry> Ordered)>
            {
                (byAlias, ordered),
            };

            if (PredicateSurvivalAnalyzer.IsUnsatisfiable(whereClause?.SearchCondition, columnRef => walker.ResolveColumnFacts(columnRef, scopeChain)))
            {
                return;
            }

            var joinNodes = fromClause.TableReferences.SelectMany(PredicateTreeWalker.FlattenJoinNodes).ToList();
            if (joinNodes.Count == 0 && fromClause.TableReferences.Count < 2)
            {
                return;
            }

            var statementWideEqualities = joinNodes
                .SelectMany(j => PredicateTreeWalker.FlattenAnd(j.SearchCondition))
                .Concat(PredicateTreeWalker.FlattenAnd(whereClause?.SearchCondition))
                .OfType<BooleanComparisonExpression>()
                .Where(c => c.ComparisonType == BooleanComparisonType.Equals)
                .ToList();

            var directlyJoinedTablePairs = new HashSet<(string, string)>(TableColumnKeyComparer.For(catalog));
            foreach (var join in joinNodes)
            {
                InspectJoin(join, byAlias, scopeChain, statementWideEqualities, directlyJoinedTablePairs);
            }

            InspectCommaJoins(ordered, statementWideEqualities, scopeChain, directlyJoinedTablePairs, fromClause);
        }

        private void InspectJoin(
            QualifiedJoin join,
            IReadOnlyDictionary<string, ScopeEntry> byAlias,
            IReadOnlyList<(IReadOnlyDictionary<string, ScopeEntry> ByAlias, IReadOnlyList<ScopeEntry> Ordered)> scopeChain,
            IReadOnlyList<BooleanComparisonExpression> statementWideEqualities,
            HashSet<(string, string)> directlyJoinedTablePairs)
        {

            var firstTable = ResolveDirectBaseTable(join.FirstTableReference, byAlias);
            var secondTable = ResolveDirectBaseTable(join.SecondTableReference, byAlias);
            if (firstTable is null || secondTable is null)
            {
                return;
            }

            directlyJoinedTablePairs.Add(NormalizedPair(firstTable, secondTable));

            var joinEqualities = PredicateTreeWalker.FlattenAnd(join.SearchCondition)
                .OfType<BooleanComparisonExpression>()
                .Where(c => c.ComparisonType == BooleanComparisonType.Equals)
                .ToList();

            foreach (var fk in FindCandidateForeignKeys(firstTable, secondTable))
            {
                TryReportFinding(fk, joinEqualities, statementWideEqualities, scopeChain, join.StartLine, join.StartColumn);
            }
        }

        private void InspectCommaJoins(
            IReadOnlyList<ScopeEntry> ordered,
            IReadOnlyList<BooleanComparisonExpression> statementWideEqualities,
            IReadOnlyList<(IReadOnlyDictionary<string, ScopeEntry> ByAlias, IReadOnlyList<ScopeEntry> Ordered)> scopeChain,
            HashSet<(string, string)> directlyJoinedTablePairs,
            FromClause fromClause)
        {
            var baseTables = ordered
                .Where(e => !e.IsViewLayer)
                .Select(e => e.Relation.QualifiedName)
                .Where(name => name is not null && catalog.Find(name)?.Kind == CatalogTableKind.Table)
                .Select(name => name!)
                .Distinct(catalog.IdentifierComparer)
                .ToList();

            for (var i = 0; i < baseTables.Count; i++)
            {
                for (var j = i + 1; j < baseTables.Count; j++)
                {
                    var pairKey = NormalizedPair(baseTables[i], baseTables[j]);
                    if (directlyJoinedTablePairs.Contains(pairKey))
                    {
                        continue;
                    }

                    foreach (var fk in FindCandidateForeignKeys(baseTables[i], baseTables[j]))
                    {

                        TryReportFinding(fk, statementWideEqualities, statementWideEqualities, scopeChain, fromClause.StartLine, fromClause.StartColumn);
                    }
                }
            }
        }

        private void TryReportFinding(
            CompositeForeignKey fk,
            IReadOnlyList<BooleanComparisonExpression> localEqualities,
            IReadOnlyList<BooleanComparisonExpression> statementWideEqualities,
            IReadOnlyList<(IReadOnlyDictionary<string, ScopeEntry> ByAlias, IReadOnlyList<ScopeEntry> Ordered)> scopeChain,
            int line, int column)
        {
            var coveredLocally = fk.Pairs.Where(p => localEqualities.Any(pred => PredicateCoversPair(pred, scopeChain, fk, p))).ToList();
            if (coveredLocally.Count == 0)
            {

                return;
            }

            var equated = EquatedColumns(statementWideEqualities, scopeChain);
            var missingEverywhere = fk.Pairs
                .Where(p => !equated.AreEqual(
                    (fk.ParentTableQualifiedName, p.ParentColumnName), (fk.ReferencedTableQualifiedName, p.ReferencedColumnName)))
                .ToList();
            if (missingEverywhere.Count == 0)
            {

                return;
            }

            if (IsSuppressedByUniqueIndex(fk, coveredLocally, statementWideEqualities, scopeChain))
            {
                return;
            }

            Findings.Add(new PartialCompositeForeignKeyJoinFinding(
                fk.ConstraintName, fk.ParentTableQualifiedName, fk.ReferencedTableQualifiedName,
                fk.Pairs, coveredLocally, missingEverywhere, sourcePath, line, column));
        }

        private ColumnEqualityClasses EquatedColumns(
            IReadOnlyList<BooleanComparisonExpression> equalities,
            IReadOnlyList<(IReadOnlyDictionary<string, ScopeEntry> ByAlias, IReadOnlyList<ScopeEntry> Ordered)> scopeChain)
        {
            var classes = new ColumnEqualityClasses(TableColumnKeyComparer.For(catalog));
            foreach (var equality in equalities)
            {
                if (BaseColumnResolver.ResolveBaseColumn(equality.FirstExpression, sourcePath, scopeChain, catalog) is { } left
                    && BaseColumnResolver.ResolveBaseColumn(equality.SecondExpression, sourcePath, scopeChain, catalog) is { } right)
                {
                    classes.Equate((left.TableQualifiedName, left.ColumnName), (right.TableQualifiedName, right.ColumnName));
                }
            }

            return classes;
        }

        private sealed class ColumnEqualityClasses(IEqualityComparer<(string Table, string Column)> comparer)
        {
            private readonly Dictionary<(string Table, string Column), (string Table, string Column)> _parents = new(comparer);

            public void Equate((string Table, string Column) left, (string Table, string Column) right)
            {
                var leftRoot = Root(left);
                var rightRoot = Root(right);
                if (!comparer.Equals(leftRoot, rightRoot))
                {
                    _parents[leftRoot] = rightRoot;
                }
            }

            public bool AreEqual((string Table, string Column) left, (string Table, string Column) right) =>
                comparer.Equals(Root(left), Root(right));

            private (string Table, string Column) Root((string Table, string Column) column)
            {
                while (_parents.TryGetValue(column, out var parent))
                {
                    column = parent;
                }

                return column;
            }
        }

        private bool IsSuppressedByUniqueIndex(
            CompositeForeignKey fk,
            IReadOnlyList<ForeignKeyColumnPair> coveredLocally,
            IReadOnlyList<BooleanComparisonExpression> statementWideEqualities,
            IReadOnlyList<(IReadOnlyDictionary<string, ScopeEntry> ByAlias, IReadOnlyList<ScopeEntry> Ordered)> scopeChain)
        {
            var referencedTable = catalog.Find(fk.ReferencedTableQualifiedName);
            if (referencedTable is null)
            {
                return false;
            }

            var usedReferencedColumns = new HashSet<string>(coveredLocally.Select(p => p.ReferencedColumnName), catalog.IdentifierComparer);
            foreach (var equality in statementWideEqualities)
            {
                foreach (var (columnSide, valueSide) in new[] { (equality.FirstExpression, equality.SecondExpression), (equality.SecondExpression, equality.FirstExpression) })
                {
                    if (valueSide is VariableReference or Literal
                        && BaseColumnResolver.ResolveBaseColumn(columnSide, sourcePath, scopeChain, catalog) is { } pinned
                        && Eq(pinned.TableQualifiedName, fk.ReferencedTableQualifiedName))
                    {
                        usedReferencedColumns.Add(pinned.ColumnName);
                    }
                }
            }

            return referencedTable.Indexes.Any(i => i.IsUnique && !i.IsFiltered && !i.IsDisabled && i.KeyColumns.Count > 0 && i.KeyColumns.All(usedReferencedColumns.Contains));
        }

        private bool PredicateCoversPair(
            BooleanComparisonExpression predicate,
            IReadOnlyList<(IReadOnlyDictionary<string, ScopeEntry> ByAlias, IReadOnlyList<ScopeEntry> Ordered)> scopeChain,
            CompositeForeignKey fk, ForeignKeyColumnPair pair)
        {
            var left = BaseColumnResolver.ResolveBaseColumn(predicate.FirstExpression, sourcePath, scopeChain, catalog);
            var right = BaseColumnResolver.ResolveBaseColumn(predicate.SecondExpression, sourcePath, scopeChain, catalog);
            if (left is null || right is null)
            {
                return false;
            }

            return (Matches(left, fk.ParentTableQualifiedName, pair.ParentColumnName) && Matches(right, fk.ReferencedTableQualifiedName, pair.ReferencedColumnName))
                || (Matches(left, fk.ReferencedTableQualifiedName, pair.ReferencedColumnName) && Matches(right, fk.ParentTableQualifiedName, pair.ParentColumnName));
        }

        private bool Matches(ColumnProvenance.BaseColumn? resolved, string table, string column) =>
            resolved is { } r
            && catalog.IdentifierComparer.Equals(r.TableQualifiedName, table)
            && catalog.IdentifierComparer.Equals(r.ColumnName, column);

        private string? ResolveDirectBaseTable(TableReference tableReference, IReadOnlyDictionary<string, ScopeEntry> byAlias)
        {
            if (tableReference is not NamedTableReference named)
            {
                return null;
            }

            var alias = named.Alias?.Value ?? named.SchemaObject.BaseIdentifier.Value;
            return byAlias.TryGetValue(alias, out var entry)
                && !entry.IsViewLayer
                && entry.Relation.QualifiedName is { } qualifiedName
                && catalog.Find(qualifiedName) is { Kind: CatalogTableKind.Table }
                ? qualifiedName
                : null;
        }

        private IEnumerable<CompositeForeignKey> FindCandidateForeignKeys(string tableA, string tableB) =>
            compositeForeignKeys.Where(fk =>
                (Eq(fk.ParentTableQualifiedName, tableA) && Eq(fk.ReferencedTableQualifiedName, tableB))
                || (Eq(fk.ParentTableQualifiedName, tableB) && Eq(fk.ReferencedTableQualifiedName, tableA)));

        private bool Eq(string a, string b) => catalog.IdentifierComparer.Equals(a, b);

        private (string, string) NormalizedPair(string a, string b) =>
            catalog.IdentifierComparer.Compare(a, b) <= 0 ? (a, b) : (b, a);

    }
}
