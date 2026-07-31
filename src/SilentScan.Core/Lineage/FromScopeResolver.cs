using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Catalog;

namespace SilentScan.Core.Lineage;

/// <summary>Resolves a FROM clause to an alias-&gt;relation scope, flattening the join tree to its leaf table references.</summary>
public static class FromScopeResolver
{
    public static (Dictionary<string, ResolvedRelation> ByAlias, List<ResolvedRelation> Ordered) Resolve(
        FromClause? fromClause, DatabaseCatalog catalog, IReadOnlyDictionary<string, ResolvedRelation> resolvedViews)
    {
        var byAlias = new Dictionary<string, ResolvedRelation>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<ResolvedRelation>();

        if (fromClause is null)
        {
            return (byAlias, ordered);
        }

        foreach (var tableReference in fromClause.TableReferences)
        {
            foreach (var leaf in FlattenJoins(tableReference))
            {
                var (alias, relation) = ResolveTableReference(leaf, catalog, resolvedViews);
                if (alias is not null)
                {
                    byAlias[alias] = relation;
                }

                ordered.Add(relation);
            }
        }

        return (byAlias, ordered);
    }

    private static IEnumerable<TableReference> FlattenJoins(TableReference tableReference)
    {
        switch (tableReference)
        {
            case JoinTableReference join:
                foreach (var t in FlattenJoins(join.FirstTableReference))
                {
                    yield return t;
                }

                foreach (var t in FlattenJoins(join.SecondTableReference))
                {
                    yield return t;
                }

                break;

            case JoinParenthesisTableReference parenthesis:
                foreach (var t in FlattenJoins(parenthesis.Join))
                {
                    yield return t;
                }

                break;

            default:
                yield return tableReference;
                break;
        }
    }

    private static (string? Alias, ResolvedRelation Relation) ResolveTableReference(
        TableReference tableReference, DatabaseCatalog catalog, IReadOnlyDictionary<string, ResolvedRelation> resolvedViews)
    {
        switch (tableReference)
        {
            case NamedTableReference named:
                var qualifiedName = SchemaObjectNameHelper.Qualify(named.SchemaObject);
                var relation = resolvedViews.TryGetValue(qualifiedName, out var view)
                    ? view
                    : ToResolvedRelation(catalog.Find(qualifiedName), qualifiedName);
                var alias = named.Alias?.Value ?? SchemaObjectNameHelper.Resolve(named.SchemaObject).Name;
                return (alias, relation);

            case QueryDerivedTable derived:
                var innerColumns = QueryExpressionResolver.Resolve(derived.QueryExpression, catalog, resolvedViews);
                if (derived.Columns.Count > 0)
                {
                    innerColumns = [.. innerColumns.Zip(derived.Columns, (c, id) => c with { Name = id.Value })];
                }

                return (derived.Alias?.Value, new ResolvedRelation(QualifiedName: null, innerColumns));

            default:
                // OPENQUERY/OPENROWSET/PIVOT/table-valued function calls etc: not yet resolved.
                // Empty columns means any reference against this alias falls through to "not found".
                return ((tableReference as TableReferenceWithAlias)?.Alias?.Value, ResolvedRelation.Empty);
        }
    }

    private static ResolvedRelation ToResolvedRelation(CatalogTable? table, string qualifiedName)
    {
        if (table is null)
        {
            // Referenced a table/view we have no DDL for - CLAUDE.md precision discipline:
            // never guess. Column lookups against this relation resolve Unknown.
            return new ResolvedRelation(qualifiedName, []);
        }

        return new ResolvedRelation(qualifiedName, [.. table.Columns.Select(c => new ResolvedColumn(
            c.Name,
            c.Type is { } type
                ? new ColumnProvenance.BaseColumn(qualifiedName, c.Name, type)
                : new ColumnProvenance.Unknown($"column {c.Name} has an unresolved declared type")))]);
    }
}
