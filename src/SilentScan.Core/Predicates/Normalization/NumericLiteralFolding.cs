using System.Globalization;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SilentScan.Core.Predicates.Normalization;

internal static class NumericLiteralFolding
{
    public static decimal? TryGetNumericLiteral(ScalarExpression expression, bool skipParentheses = false)
    {
        if (skipParentheses)
        {
            while (expression is ParenthesisExpression parenthesis)
            {
                expression = parenthesis.Expression;
            }
        }

        return expression switch
        {
            IntegerLiteral lit => ParseDecimal(lit.Value),
            NumericLiteral lit => ParseDecimal(lit.Value),
            MoneyLiteral lit => ParseDecimal(lit.Value),

            UnaryExpression { UnaryExpressionType: UnaryExpressionType.Negative } unary =>
                TryGetNumericLiteral(unary.Expression, skipParentheses) is { } v ? -v : null,
            UnaryExpression { UnaryExpressionType: UnaryExpressionType.Positive } unary =>
                TryGetNumericLiteral(unary.Expression, skipParentheses),
            _ => null,
        };
    }

    public static NumericValueRangeSet ToRangeSet(CmpOp op, decimal value) => op switch
    {
        CmpOp.Eq => NumericValueRangeSet.ForEquals(value),
        CmpOp.Ne => NumericValueRangeSet.ForNotEquals(value),
        CmpOp.Lt => NumericValueRangeSet.ForLessThan(value),
        CmpOp.Le => NumericValueRangeSet.ForLessThanOrEqual(value),
        CmpOp.Gt => NumericValueRangeSet.ForGreaterThan(value),
        CmpOp.Ge => NumericValueRangeSet.ForGreaterThanOrEqual(value),
        _ => NumericValueRangeSet.Universal,
    };

    private static decimal? ParseDecimal(string value) =>
        decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
}
