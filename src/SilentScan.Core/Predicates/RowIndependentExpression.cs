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

        return value switch
        {
            Literal or VariableReference or GlobalVariableExpression => true,
            BinaryExpression binary => Is(binary.FirstExpression) && Is(binary.SecondExpression),
            _ => false,
        };
    }
}
