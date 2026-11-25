using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SilentScan.Core.Predicates;

internal static class RowIndependentExpression
{
    private static readonly HashSet<string> AggregateNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "COUNT", "COUNT_BIG", "SUM", "AVG", "MIN", "MAX", "STDEV", "STDEVP", "VAR", "VARP",
    };

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

    public static bool OrderKeysAreAllRowIndependent(IList<ExpressionWithSortOrder> elements) =>
        elements.Count > 0 && elements.All(element => OrderKeyIsRowIndependent(element.Expression));

    public static bool OrderKeysAreAllRowIndependent(IList<ExpressionWithSortOrder> elements, IList<SelectElement> selectList) =>
        elements.Count > 0 && elements.All(element => AliasedKeyIsRowIndependent(element.Expression, selectList));

    private static bool AliasedKeyIsRowIndependent(ScalarExpression key, IList<SelectElement> selectList)
    {
        var resolved = ThroughSelectAlias(key, selectList);
        return ReferenceEquals(resolved, key) ? OrderKeyIsRowIndependent(key) : Is(resolved);
    }

    private static ScalarExpression ThroughSelectAlias(ScalarExpression key, IList<SelectElement> selectList)
    {
        if (key is not ColumnReferenceExpression { MultiPartIdentifier.Identifiers: [{ Value: var name }] })
        {
            return key;
        }

        var matches = selectList.OfType<SelectScalarExpression>()
            .Where(e => e.ColumnName?.Value is { } alias && string.Equals(alias, name, StringComparison.Ordinal))
            .ToList();
        return matches.Count == 1 ? matches[0].Expression : key;
    }

    public static bool IsAggregateOnly(ScalarExpression value)
    {
        while (value is ParenthesisExpression parenthesis)
        {
            value = parenthesis.Expression;
        }

        return value switch
        {
            Literal or VariableReference or GlobalVariableExpression => true,
            FunctionCall { OverClause: null } call when AggregateNames.Contains(call.FunctionName.Value) => true,
            FunctionCall { OverClause: null } call => call.Parameters.All(IsAggregateOnly),
            BinaryExpression binary => IsAggregateOnly(binary.FirstExpression) && IsAggregateOnly(binary.SecondExpression),
            UnaryExpression unary => IsAggregateOnly(unary.Expression),
            _ => false,
        };
    }

    private static bool OrderKeyIsRowIndependent(ScalarExpression key)
    {
        while (key is ParenthesisExpression parenthesis)
        {
            key = parenthesis.Expression;
        }

        return key switch
        {
            ScalarSubquery { QueryExpression: QuerySpecification { FromClause: null, SelectElements: [SelectScalarExpression { Expression: Literal }] } } => true,
            _ => Is(key) && NamesAVariable(key),
        };
    }

    private static bool NamesAVariable(ScalarExpression value)
    {
        while (value is ParenthesisExpression parenthesis)
        {
            value = parenthesis.Expression;
        }

        return value switch
        {
            VariableReference or GlobalVariableExpression => true,
            BinaryExpression binary => NamesAVariable(binary.FirstExpression) || NamesAVariable(binary.SecondExpression),
            _ => false,
        };
    }
}
