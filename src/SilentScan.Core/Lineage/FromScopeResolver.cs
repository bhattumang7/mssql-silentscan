using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Catalog;

namespace SilentScan.Core.Lineage;

/// <summary>Resolves a FROM clause to an alias-&gt;relation scope, flattening the join tree to its leaf table references.</summary>
public static class FromScopeResolver
{
    public static (Dictionary<string, ScopeEntry> ByAlias, List<ScopeEntry> Ordered) Resolve(
        FromClause? fromClause, DatabaseCatalog catalog, IReadOnlyDictionary<string, ResolvedRelation> resolvedViews, string sourcePath)
    {
        var byAlias = new Dictionary<string, ScopeEntry>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<ScopeEntry>();

        if (fromClause is null)
        {
            return (byAlias, ordered);
        }

        foreach (var tableReference in fromClause.TableReferences)
        {
            foreach (var leaf in FlattenJoins(tableReference))
            {
                var (alias, entry) = ResolveTableReference(leaf, catalog, resolvedViews, sourcePath);
                if (alias is not null)
                {
                    byAlias[alias] = entry;
                }

                ordered.Add(entry);
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

    private static (string? Alias, ScopeEntry Entry) ResolveTableReference(
        TableReference tableReference, DatabaseCatalog catalog, IReadOnlyDictionary<string, ResolvedRelation> resolvedViews, string sourcePath)
    {
        switch (tableReference)
        {
            case NamedTableReference named:
                var qualifiedName = SchemaObjectNameHelper.Qualify(named.SchemaObject);
                var isViewLayer = resolvedViews.TryGetValue(qualifiedName, out var view);
                var relation = isViewLayer ? view! : ToResolvedRelation(catalog.Find(qualifiedName), qualifiedName);
                var alias = named.Alias?.Value ?? SchemaObjectNameHelper.Resolve(named.SchemaObject).Name;
                return (alias, new ScopeEntry(relation, isViewLayer));

            case QueryDerivedTable derived:
                // A derived-table subquery is inline, local to this statement - not a
                // persisted view/TVF, so it does not add view-layer depth.
                var innerColumns = QueryExpressionResolver.Resolve(derived.QueryExpression, catalog, resolvedViews, sourcePath);
                if (derived.Columns.Count > 0)
                {
                    innerColumns = [.. innerColumns.Zip(derived.Columns, (c, id) => c with { Name = id.Value })];
                }

                return (derived.Alias?.Value, new ScopeEntry(new ResolvedRelation(QualifiedName: null, innerColumns), IsViewLayer: false));

            default:
                // OPENQUERY/OPENROWSET/PIVOT/table-valued function calls etc: not yet resolved.
                // Empty columns means any reference against this alias falls through to "not found".
                return ((tableReference as TableReferenceWithAlias)?.Alias?.Value, new ScopeEntry(ResolvedRelation.Empty, IsViewLayer: false));
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
