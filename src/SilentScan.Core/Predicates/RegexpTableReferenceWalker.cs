using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SilentScan.Core.Predicates;

internal static class RegexpTableReferenceWalker
{
    internal static void ForEachFunctionReference(FromClause fromClause, Action<GlobalFunctionTableReference> inspect)
    {
        foreach (var tableReference in fromClause.TableReferences)
        {
            Visit(tableReference, inspect);
        }
    }

    private static void Visit(TableReference tableReference, Action<GlobalFunctionTableReference> inspect)
    {
        switch (tableReference)
        {
            case JoinTableReference join:
                Visit(join.FirstTableReference, inspect);
                Visit(join.SecondTableReference, inspect);
                break;

            case JoinParenthesisTableReference parenthesis:
                Visit(parenthesis.Join, inspect);
                break;

            case GlobalFunctionTableReference function:
                inspect(function);
                break;
        }
    }
}
