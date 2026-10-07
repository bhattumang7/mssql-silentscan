using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SilentScan.Core.Predicates;

internal static class NullImplication
{
    public static IReadOnlyList<ColumnReferenceExpression> NonNullWhenTrue(BooleanExpression expression) =>
        [.. TrueSet(expression).Values];

    public static IReadOnlyList<ColumnReferenceExpression> NonNullWhenFalse(BooleanExpression expression) =>
        [.. FalseSet(expression).Values];

    private static Dictionary<string, ColumnReferenceExpression> TrueSet(BooleanExpression expression)
    {
        switch (expression)
        {
            case BooleanParenthesisExpression parenthesis:
                return TrueSet(parenthesis.Expression);
            case BooleanNotExpression not:
                return FalseSet(not.Expression);
            case BooleanIsNullExpression { IsNot: true } isNotNull:
                return Single(isNotNull.Expression);
            case BooleanComparisonExpression comparison:
                return Merge(Single(comparison.FirstExpression), Single(comparison.SecondExpression));
            case BooleanBinaryExpression { BinaryExpressionType: BooleanBinaryExpressionType.And } and:
                return Merge(TrueSet(and.FirstExpression), TrueSet(and.SecondExpression));
            case BooleanBinaryExpression { BinaryExpressionType: BooleanBinaryExpressionType.Or } or:
                return Intersect(TrueSet(or.FirstExpression), TrueSet(or.SecondExpression));
            default:
                return [];
        }
    }

    private static Dictionary<string, ColumnReferenceExpression> FalseSet(BooleanExpression expression)
    {
        switch (expression)
        {
            case BooleanParenthesisExpression parenthesis:
                return FalseSet(parenthesis.Expression);
            case BooleanNotExpression not:
                return TrueSet(not.Expression);
            case BooleanIsNullExpression { IsNot: false } isNull:
                return Single(isNull.Expression);
            case BooleanBinaryExpression { BinaryExpressionType: BooleanBinaryExpressionType.Or } or:
                return Merge(FalseSet(or.FirstExpression), FalseSet(or.SecondExpression));
            case BooleanBinaryExpression { BinaryExpressionType: BooleanBinaryExpressionType.And } and:
                return Intersect(FalseSet(and.FirstExpression), FalseSet(and.SecondExpression));
            default:
                return [];
        }
    }

    private static Dictionary<string, ColumnReferenceExpression> Single(ScalarExpression expression)
    {
        while (expression is ParenthesisExpression parenthesis)
        {
            expression = parenthesis.Expression;
        }

        return expression is ColumnReferenceExpression { MultiPartIdentifier.Identifiers: { Count: > 0 } identifiers } column
            ? new Dictionary<string, ColumnReferenceExpression>(StringComparer.OrdinalIgnoreCase)
            {
                [string.Join('.', identifiers.Select(i => i.Value))] = column,
            }
            : [];
    }

    private static Dictionary<string, ColumnReferenceExpression> Merge(
        Dictionary<string, ColumnReferenceExpression> left, Dictionary<string, ColumnReferenceExpression> right)
    {
        var merged = new Dictionary<string, ColumnReferenceExpression>(left, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in right)
        {
            merged[key] = value;
        }

        return merged;
    }

    private static Dictionary<string, ColumnReferenceExpression> Intersect(
        Dictionary<string, ColumnReferenceExpression> left, Dictionary<string, ColumnReferenceExpression> right) =>
        left.Where(pair => right.ContainsKey(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
}
