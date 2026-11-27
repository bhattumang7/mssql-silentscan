using System.Globalization;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SilentScan.Core.Common;

internal static class TopPercentLiteralHelper
{
    public static bool IsHundredPercentLiteral(ScalarExpression expression) =>
        Unwrap(expression) is IntegerLiteral or NumericLiteral or RealLiteral
        && double.TryParse(((Literal)Unwrap(expression)).Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent)
        && percent.Equals(100d);

    public static bool ReturnsAtMostOneRow(TopRowFilter? topRowFilter) =>
        topRowFilter is { Percent: false, WithTies: false } filter && Unwrap(filter.Expression) is IntegerLiteral { Value: "1" };

    private static ScalarExpression Unwrap(ScalarExpression expression) =>
        expression is ParenthesisExpression parenthesis ? Unwrap(parenthesis.Expression) : expression;
}
