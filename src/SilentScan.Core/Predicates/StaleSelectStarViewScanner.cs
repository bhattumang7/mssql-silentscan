using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Catalog;
using SilentScan.Core.Diagnostics;
using SilentScan.Core.Lineage;
using SilentScan.Core.Common;

namespace SilentScan.Core.Predicates;

public static class StaleSelectStarViewScanner
{
    public static IReadOnlyList<StaleSelectStarViewFinding> Scan(IReadOnlyList<ViewDefinition> views, DatabaseCatalog catalog, IScanStage? stage = null)
    {
        var findings = new List<StaleSelectStarViewFinding>();

        foreach (var view in views)
        {
            stage?.Advance(currentItem: view.QualifiedName);

            if (FindOutermostStarLine(view.SelectStatement.QueryExpression) is null)
            {
                continue;
            }

            var cteNames = CteNameHelper.Names(view.SelectStatement.WithCtesAndXmlNamespaces, catalog.IdentifierComparer);
            if (FindSingleBaseTable(view.SelectStatement.QueryExpression, cteNames) is not { } baseTableQualifiedName)
            {
                continue;
            }

            if (!catalog.TryGetViewCompiledColumns(view.QualifiedName, out var viewColumns))
            {
                continue;
            }

            var baseTable = catalog.Find(baseTableQualifiedName);
            if (baseTable is null)
            {
                continue;
            }

            var baseTableColumns = baseTable.Columns.Where(c => !c.IsHidden).Select(c => c.Name).ToList();

            if (ExtraColumnNames(view.SelectStatement.QueryExpression) is not { } extraNames
                || extraNames.Any(extra => baseTableColumns.Contains(extra, catalog.IdentifierComparer)))
            {
                continue;
            }

            var starExpandedColumns = viewColumns.Where(column => !extraNames.Contains(column, catalog.IdentifierComparer)).ToList();
            if (starExpandedColumns.SequenceEqual(baseTableColumns, catalog.IdentifierComparer))
            {
                continue;
            }

            findings.Add(new StaleSelectStarViewFinding(
                view.QualifiedName, baseTableQualifiedName, viewColumns, baseTableColumns, view.SourcePath, view.SourceLine));
        }

        return
        [
            .. findings
                .OrderBy(f => f.ViewQualifiedName, StringComparer.Ordinal)
                .ThenBy(f => f.BaseTableQualifiedName, StringComparer.Ordinal),
        ];
    }

    private static List<string>? ExtraColumnNames(QueryExpression queryExpression)
    {
        switch (queryExpression)
        {
            case QueryParenthesisExpression parenthesis:
                return ExtraColumnNames(parenthesis.QueryExpression);
            case QuerySpecification spec:
                var names = new List<string>();
                foreach (var element in spec.SelectElements.Where(e => e is not SelectStarExpression))
                {
                    var name = element switch
                    {
                        SelectScalarExpression { ColumnName.Value: { } alias } => alias,
                        SelectScalarExpression { Expression: ColumnReferenceExpression { MultiPartIdentifier.Identifiers: { Count: > 0 } identifiers } } => identifiers[^1].Value,
                        _ => null,
                    };
                    if (name is null)
                    {
                        return null;
                    }

                    names.Add(name);
                }

                return names;
            default:
                return null;
        }
    }

    private static int? FindOutermostStarLine(QueryExpression queryExpression) =>
        queryExpression switch
        {
            QueryParenthesisExpression parenthesis => FindOutermostStarLine(parenthesis.QueryExpression),
            QuerySpecification spec => spec.SelectElements.OfType<SelectStarExpression>().Select(s => (int?)s.StartLine).FirstOrDefault(),
            _ => null,
        };

    private static string? FindSingleBaseTable(QueryExpression queryExpression, HashSet<string> cteNames) =>
        queryExpression switch
        {
            QueryParenthesisExpression parenthesis => FindSingleBaseTable(parenthesis.QueryExpression, cteNames),
            QuerySpecification { FromClause.TableReferences: [NamedTableReference namedTable] }
                when namedTable.SchemaObject.SchemaIdentifier is not null || !cteNames.Contains(namedTable.SchemaObject.BaseIdentifier.Value) =>
                SchemaObjectNameHelper.Qualify(namedTable.SchemaObject),
            _ => null,
        };
}
