using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Catalog;
using SilentScan.Core.Lineage;
using SilentScan.Core.Predicates.Normalization;

namespace SilentScan.Core.Predicates;

internal sealed record ConstrainedStatement(
    IReadOnlyList<CatalogTable> BaseTables,
    HashSet<ColumnProvenance.BaseColumn> AndConstrainedColumns,
    HashSet<ColumnProvenance.BaseColumn> AndEqualityConstrainedColumns,
    HashSet<ColumnProvenance.BaseColumn> RowFilterColumns,
    HashSet<ColumnProvenance.BaseColumn> RowFilterEqualityColumns,
    IReadOnlyList<(IReadOnlyDictionary<string, ScopeEntry> ByAlias, IReadOnlyList<ScopeEntry> Ordered)> ScopeChain,
    IReadOnlyList<QualifiedJoin> JoinNodes,
    BooleanExpression? WhereCondition,
    TSqlFragment Node,
    ModuleWalker Walker)
{
    public bool WhereConditionIsUnsatisfiable() =>
        PredicateSurvivalAnalyzer.IsUnsatisfiable(
            WhereCondition, columnRef => Walker.ResolveColumnFacts(columnRef, ScopeChain));
}

internal abstract class ConstrainedColumnStatementVisitor(string sourcePath, DatabaseCatalog catalog) : IModuleRule
{
    private static readonly IReadOnlyDictionary<string, ResolvedRelation> EmptyResolvedViews = new Dictionary<string, ResolvedRelation>();

    protected string SourcePath { get; } = sourcePath;

    protected DatabaseCatalog Catalog { get; } = catalog;

    protected abstract void InspectStatement(ConstrainedStatement statement);

    public void OnEnterQuerySpecificationScope(QuerySpecification node, ScopeChain scopeChain, ModuleWalker walker) =>
        Inspect(node.FromClause, node.WhereClause?.SearchCondition, node, walker);

    public void OnEnterUpdateStatementScope(UpdateStatement node, ScopeChain scopeChain, ModuleWalker walker)
    {
        var spec = node.UpdateSpecification;
        var cteRelations = CteResolver.Resolve(node.WithCtesAndXmlNamespaces, Catalog, EmptyResolvedViews, SourcePath, ledger: null);
        var (byAlias, ordered) = FromScopeResolver.ResolveForDataModification(spec.Target, spec.FromClause, ResolutionContext(cteRelations));
        Inspect(byAlias, ordered, spec.FromClause, spec.WhereClause?.SearchCondition, node, walker);
    }

    public void OnEnterDeleteStatementScope(DeleteStatement node, ScopeChain scopeChain, ModuleWalker walker)
    {
        var spec = node.DeleteSpecification;
        var cteRelations = CteResolver.Resolve(node.WithCtesAndXmlNamespaces, Catalog, EmptyResolvedViews, SourcePath, ledger: null);
        var (byAlias, ordered) = FromScopeResolver.ResolveForDataModification(spec.Target, spec.FromClause, ResolutionContext(cteRelations));
        Inspect(byAlias, ordered, spec.FromClause, spec.WhereClause?.SearchCondition, node, walker);
    }

    private FromScopeResolver.ResolutionContext ResolutionContext(IReadOnlyDictionary<string, ResolvedRelation> cteRelations) =>
        new(Catalog, EmptyResolvedViews, SourcePath, Ledger: null, cteRelations, ProcScope: null);

    private void Inspect(FromClause? fromClause, BooleanExpression? whereCondition, TSqlFragment node, ModuleWalker walker)
    {
        if (fromClause is null)
        {
            return;
        }

        var (byAlias, ordered) = FromScopeResolver.Resolve(fromClause, ResolutionContext(walker.CurrentCteRelations()));
        Inspect(byAlias, ordered, fromClause, whereCondition, node, walker);
    }

    private void Inspect(
        IReadOnlyDictionary<string, ScopeEntry> byAlias, IReadOnlyList<ScopeEntry> ordered,
        FromClause? fromClause, BooleanExpression? whereCondition, TSqlFragment node, ModuleWalker walker)
    {
        var baseTables = ordered
            .Where(e => !e.IsViewLayer && e.Relation.QualifiedName is not null)
            .Select(e => e.Relation.QualifiedName!)
            .Distinct(Catalog.IdentifierComparer)
            .Select(name => Catalog.Find(name))
            .Where(t => t is not null && t.Kind == CatalogTableKind.Table)
            .Select(t => t!)
            .ToList();

        if (baseTables.Count == 0)
        {
            return;
        }

        var scopeChain = new List<(IReadOnlyDictionary<string, ScopeEntry> ByAlias, IReadOnlyList<ScopeEntry> Ordered)> { (byAlias, ordered) };
        var joinNodes = fromClause is null ? [] : fromClause.TableReferences.SelectMany(PredicateTreeWalker.FlattenJoinNodes).ToList();

        var andComparisons = joinNodes
            .SelectMany(j => PredicateTreeWalker.FlattenAnd(j.SearchCondition))
            .Concat(PredicateTreeWalker.FlattenAnd(whereCondition))
            .OfType<BooleanComparisonExpression>()
            .ToList();

        var andConstrainedColumns = andComparisons
            .SelectMany(c => BaseColumnResolver.ResolveBothSides(c, SourcePath, scopeChain, Catalog))
            .ToHashSet(TableColumnKeyComparer.For(Catalog));

        var andEqualityConstrainedColumns = andComparisons
            .Where(c => c.ComparisonType == BooleanComparisonType.Equals)
            .SelectMany(c => BaseColumnResolver.ResolveAgainstColumnFreeSide(c, SourcePath, scopeChain, Catalog))
            .ToHashSet(TableColumnKeyComparer.For(Catalog));

        var rowFilterComparisons = joinNodes
            .SelectMany(j => PredicateTreeWalker.FlattenAnd(j.SearchCondition)
                .OfType<BooleanComparisonExpression>()
                .Where(c => !IsConstantOnPreservedSide(c, j)))
            .Concat(PredicateTreeWalker.FlattenAnd(whereCondition).OfType<BooleanComparisonExpression>())
            .ToList();

        var rowFilterColumns = rowFilterComparisons
            .SelectMany(c => BaseColumnResolver.ResolveBothSides(c, SourcePath, scopeChain, Catalog))
            .ToHashSet(TableColumnKeyComparer.For(Catalog));

        var rowFilterEqualityColumns = rowFilterComparisons
            .Where(c => c.ComparisonType == BooleanComparisonType.Equals)
            .SelectMany(c => BaseColumnResolver.ResolveAgainstColumnFreeSide(c, SourcePath, scopeChain, Catalog))
            .ToHashSet(TableColumnKeyComparer.For(Catalog));

        InspectStatement(new ConstrainedStatement(
            baseTables, andConstrainedColumns, andEqualityConstrainedColumns, rowFilterColumns, rowFilterEqualityColumns,
            scopeChain, joinNodes, whereCondition, node, walker));
    }

    private bool IsConstantOnPreservedSide(BooleanComparisonExpression comparison, QualifiedJoin join)
    {
        var preservedBranches = join.QualifiedJoinType switch
        {
            QualifiedJoinType.LeftOuter => new[] { join.FirstTableReference },
            QualifiedJoinType.RightOuter => new[] { join.SecondTableReference },
            QualifiedJoinType.FullOuter => new[] { join.FirstTableReference, join.SecondTableReference },
            _ => [],
        };

        if (preservedBranches.Length == 0)
        {
            return false;
        }

        var firstFree = !BaseColumnResolver.ContainsColumnReference(comparison.FirstExpression);
        var secondFree = !BaseColumnResolver.ContainsColumnReference(comparison.SecondExpression);
        if (firstFree == secondFree)
        {
            return false;
        }

        var collector = new ColumnAliasHelpers.RawColumnReferenceCollector();
        (firstFree ? comparison.SecondExpression : comparison.FirstExpression).Accept(collector);

        var preservedAliases = preservedBranches
            .SelectMany(PredicateTreeWalker.FlattenNamedTables)
            .Select(n => n.Alias?.Value ?? n.SchemaObject.BaseIdentifier.Value)
            .ToHashSet(Catalog.IdentifierComparer);

        return collector.References.All(r =>
            r.MultiPartIdentifier is not { Identifiers: { Count: >= 2 } ids } || preservedAliases.Contains(ids[^2].Value));
    }
}
