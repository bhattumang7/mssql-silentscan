using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SilentScan.Core.Predicates;

internal static class RowIndependentExpression
{
    public static bool Is(ScalarExpression value)
    {
        while (value is ParenthesisExpression parenthesis)
        {
            value = parenthesis.Expression;
        }

        return value is Literal or VariableReference or GlobalVariableExpression;
    }
}
