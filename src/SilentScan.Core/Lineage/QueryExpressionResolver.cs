using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Catalog;
using SilentScan.Core.Diagnostics;
using SilentScan.Core.TypeInference;

namespace SilentScan.Core.Lineage;

public sealed record QueryResolutionScope(
    IReadOnlyDictionary<string, ResolvedRelation>? CteRelations = null,
    string? ProcScope = null,
    IReadOnlyList<(IReadOnlyDictionary<string, ScopeEntry> ByAlias, IReadOnlyList<ScopeEntry> Ordered)>? OuterScopes = null);

public static class QueryExpressionResolver
{
    private static readonly IReadOnlyDictionary<string, ResolvedRelation> EmptyCteRelations = new Dictionary<string, ResolvedRelation>();

    private static readonly SqlType ForClauseTextType = new(SqlTypeCategory.NVarChar, IsMax: true);

    public static List<ResolvedColumn> Resolve(
        QueryExpression queryExpression,
        DatabaseCatalog catalog,
        IReadOnlyDictionary<string, ResolvedRelation> resolvedViews,
        string sourcePath,
        SkipLedger? ledger = null,
        QueryResolutionScope? scope = null) =>
        queryExpression switch
        {
            QuerySpecification spec => ResolveQuerySpecification(spec, catalog, resolvedViews, sourcePath, ledger, scope),
            BinaryQueryExpression binary => ResolveBinary(binary, catalog, resolvedViews, sourcePath, ledger, scope),
            QueryParenthesisExpression parenthesis => Resolve(parenthesis.QueryExpression, catalog, resolvedViews, sourcePath, ledger, scope),
            _ => ResolveUnsupported(queryExpression, sourcePath, ledger),
        };

    private static List<ResolvedColumn> ResolveUnsupported(QueryExpression queryExpression, string sourcePath, SkipLedger? ledger)
    {
        ledger?.Record(
            AnalysisPass.Lineage, sourcePath, queryExpression.StartLine, queryExpression.StartColumn,
            "query expression", $"unsupported query expression kind '{queryExpression.GetType().Name}' - resolved to zero columns");
        return [];
    }

    private static List<ResolvedColumn> ResolveBinary(
        BinaryQueryExpression binary,
        DatabaseCatalog catalog,
        IReadOnlyDictionary<string, ResolvedRelation> resolvedViews,
        string sourcePath,
        SkipLedger? ledger,
        QueryResolutionScope? scope)
    {
        var first = Resolve(binary.FirstQueryExpression, catalog, resolvedViews, sourcePath, ledger, scope);
        var second = Resolve(binary.SecondQueryExpression, catalog, resolvedViews, sourcePath, ledger, scope);

        if (first.Count != second.Count)
        {

            ledger?.Record(
                AnalysisPass.Lineage, sourcePath, binary.StartLine, binary.StartColumn, "query expression",
                $"UNION/EXCEPT/INTERSECT branches resolved different column counts ({first.Count} vs {second.Count}) - column identity can't be trusted");
            var longer = first.Count > second.Count ? first : second;
            return [.. longer.Select(c => new ResolvedColumn(c.Name, new ColumnProvenance.Unknown("UNION/EXCEPT/INTERSECT branches resolved different column counts")))];
        }

        return [.. first.Zip(second, (f, s) => new ResolvedColumn(f.Name, new ColumnProvenance.Union([f.Provenance, s.Provenance])))];
    }

    private static List<ResolvedColumn> ResolveQuerySpecification(
        QuerySpecification spec,
        DatabaseCatalog catalog,
        IReadOnlyDictionary<string, ResolvedRelation> resolvedViews,
        string sourcePath,
        SkipLedger? ledger,
        QueryResolutionScope? scope)
    {
        var outerScopes = scope?.OuterScopes;
        var (byAlias, ordered) = FromScopeResolver.Resolve(
            spec.FromClause,
            new FromScopeResolver.ResolutionContext(catalog, resolvedViews, sourcePath, ledger, scope?.CteRelations ?? EmptyCteRelations, scope?.ProcScope, OuterScopes: outerScopes));
        IReadOnlyList<(IReadOnlyDictionary<string, ScopeEntry> ByAlias, IReadOnlyList<ScopeEntry> Ordered)> scopeChain = [(byAlias, ordered), .. outerScopes ?? []];
        var result = new List<ResolvedColumn>();

        foreach (var element in spec.SelectElements)
        {
            switch (element)
            {
                case SelectStarExpression star:
                    result.AddRange(ResolveStar(star, byAlias, ordered, sourcePath, ledger));
                    break;

                case SelectScalarExpression scalar:
                    var name = scalar.ColumnName?.Value ?? InferName(scalar.Expression);
                    var provenance = ScalarExpressionResolver.Resolve(scalar.Expression, scopeChain, sourcePath, ledger, catalog.TypeAliases, catalog);
                    if (EmitsSuperAggregateNulls(spec.GroupByClause) && provenance is ColumnProvenance.BaseColumn grouped)
                    {
                        provenance = grouped with { IsNullableSide = true };
                    }

                    result.Add(new ResolvedColumn(name ?? "?column?", provenance));
                    break;
            }
        }

        return spec.ForClause is XmlForClause or JsonForClause ? [ForClauseResultColumn(spec.ForClause)] : result;
    }

    private static ResolvedColumn ForClauseResultColumn(ForClause forClause)
    {
        var isXmlType = forClause is XmlForClause { Options: { } options } && options.Any(o => o.OptionKind == XmlForClauseOptions.Type);
        var (name, type) = (forClause, isXmlType) switch
        {
            (XmlForClause, true) => ("XML_F52E2B61-18A1-11d1-B105-00805F49916B", new SqlType(SqlTypeCategory.Xml)),
            (XmlForClause, false) => ("XML_F52E2B61-18A1-11d1-B105-00805F49916B", ForClauseTextType),
            _ => ("JSON_F52E2B61-18A1-11d1-B105-00805F49916B", ForClauseTextType),
        };

        return new ResolvedColumn(name, new ColumnProvenance.Expression(type, Inputs: []));
    }

    private static bool EmitsSuperAggregateNulls(GroupByClause? groupBy) =>
        groupBy is not null
        && (groupBy.GroupByOption is GroupByOption.Rollup or GroupByOption.Cube
            || groupBy.GroupingSpecifications.Any(specification =>
                specification is RollupGroupingSpecification or CubeGroupingSpecification or GrandTotalGroupingSpecification or GroupingSetsGroupingSpecification));

    private static IEnumerable<ResolvedColumn> ResolveStar(
        SelectStarExpression star, Dictionary<string, ScopeEntry> byAlias, IReadOnlyList<ScopeEntry> ordered, string sourcePath, SkipLedger? ledger)
    {
        if (star.Qualifier is { Count: > 0 } qualifier)
        {
            var aliasName = qualifier.Identifiers[^1].Value;
            if (byAlias.TryGetValue(aliasName, out var entry))
            {
                return BumpAll(entry);
            }

            ledger?.Record(AnalysisPass.Lineage, sourcePath, star.StartLine, star.StartColumn, "SELECT *", $"unknown table alias '{aliasName}' in SELECT {aliasName}.*");
            return [new ResolvedColumn("*", new ColumnProvenance.Unknown($"unknown table alias '{aliasName}' in SELECT *"))];
        }

        return ordered.SelectMany(BumpAll);
    }

    private static IEnumerable<ResolvedColumn> BumpAll(ScopeEntry entry) =>
        entry.Relation.Columns.Select(c => c with { Provenance = ScalarExpressionResolver.BumpDepthIfViewLayer(c.Provenance, entry.IsViewLayer) });

    private static string? InferName(ScalarExpression expression) =>
        expression is ColumnReferenceExpression { MultiPartIdentifier.Identifiers: [.., { Value: { } lastIdentifier }] }
            ? lastIdentifier
            : null;
}
