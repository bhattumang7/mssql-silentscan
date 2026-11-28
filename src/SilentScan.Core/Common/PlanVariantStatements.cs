using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SilentScan.Core.Common;

internal sealed class PlanVariantStatements
{
    private readonly HashSet<SelectStatement> _cursorQueries = [];

    public void Record(DeclareCursorStatement node)
    {
        if (node.CursorDefinition.Select is { } query)
        {
            _cursorQueries.Add(query);
        }
    }

    public bool CanHaveVariants(StatementWithCtesAndXmlNamespaces statement) =>
        statement is not SelectStatement select
        || !_cursorQueries.Contains(select)
            && !(select.QueryExpression is QuerySpecification query && query.SelectElements.OfType<SelectSetVariable>().Any());
}
