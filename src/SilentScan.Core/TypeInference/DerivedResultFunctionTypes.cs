using System.Globalization;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SilentScan.Core.TypeInference;

public static class DerivedResultFunctionTypes
{
    private const int NonUnicodeCap = 8000;
    private const int UnicodeCap = 4000;
    private const int Utf8CaseMappingFactor = 8;
    private const int DefaultStrLength = 10;

    private static readonly HashSet<string> ValueOfFirstArgument = new(StringComparer.OrdinalIgnoreCase)
    {
        "LAG", "LEAD", "FIRST_VALUE", "LAST_VALUE",
    };

    private static readonly HashSet<string> BoundedComparisonFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "GREATEST", "LEAST",
    };

    private static readonly HashSet<string> Utf8CaseMapping = new(StringComparer.OrdinalIgnoreCase)
    {
        "UPPER", "LOWER",
    };

    private static readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        "LAG", "LEAD", "FIRST_VALUE", "LAST_VALUE", "GREATEST", "LEAST", "STR", "REPLICATE", "CONCAT", "CONCAT_WS", "JSON_QUERY", "DATETRUNC", "DATE_BUCKET",
    };

    public static bool IsDerived(string functionName) => Names.Contains(functionName);

    public static IReadOnlyCollection<string> FunctionNames => Names;

    public static bool IsValueOfFirstArgument(string functionName) => ValueOfFirstArgument.Contains(functionName);

    public static bool IsBoundedComparison(string functionName) => BoundedComparisonFunctions.Contains(functionName);

    public static SqlType? BoundComparisonResult(SqlType? combined) => combined switch
    {
        { IsMax: true, IsStringFamily: true } => combined with { IsMax = false, Length = combined.IsUnicodeString ? UnicodeCap : NonUnicodeCap },
        { IsLegacyLob: true } => null,
        _ => combined,
    };

    public static SqlType? Resolve(string functionName, IList<ScalarExpression> parameters, Func<ScalarExpression, SqlType?> resolve) =>
        functionName.ToUpperInvariant() switch
        {
            "STR" => ResolveStr(parameters),
            "REPLICATE" when parameters.Count == 2 => ResolveReplicate(resolve(parameters[0]), parameters[1]),
            "CONCAT" when parameters.Count >= 2 => ResolveConcat([.. parameters.Select(resolve)], separator: null),
            "CONCAT_WS" when parameters.Count >= 3 => ResolveConcat([.. parameters.Skip(1).Select(resolve)], resolve(parameters[0])),
            "JSON_QUERY" when parameters.Count >= 1 => resolve(parameters[0]) is { IsStringFamily: true } json
                ? new SqlType(SqlTypeCategory.NVarChar, Length: json.IsMax ? null : UnicodeCap, IsMax: json.IsMax)
                : null,
            "DATETRUNC" when parameters.Count == 2 => DateTruncated(resolve(parameters[1])),
            "DATE_BUCKET" when parameters.Count >= 3 => resolve(parameters[2]) is { IsDateTimeFamily: true } bucketed ? bucketed : null,
            _ => null,
        };

    public static SqlType? AdjustCaseMapping(string functionName, SqlType? result) =>
        Utf8CaseMapping.Contains(functionName)
        && result is { IsNonUnicodeString: true, IsMax: false, IsLegacyLob: false, Length: { } length, Collation.IsUtf8: true }
            ? result with { Category = SqlTypeCategory.VarChar, Length = Math.Min(length * Utf8CaseMappingFactor, NonUnicodeCap) }
            : result;

    private static SqlType? DateTruncated(SqlType? argument) => argument switch
    {
        { IsDateTimeFamily: true } => argument,
        { IsStringFamily: true } => new SqlType(SqlTypeCategory.DateTime2, Scale: 7),
        _ => null,
    };

    private static SqlType ResolveStr(IList<ScalarExpression> parameters)
    {
        if (parameters.Count == 1)
        {
            return new SqlType(SqlTypeCategory.VarChar, Length: DefaultStrLength);
        }

        return PositiveIntegerLiteral(parameters[1]) is { } length
            ? new SqlType(SqlTypeCategory.VarChar, Length: Math.Min(length, NonUnicodeCap))
            : new SqlType(SqlTypeCategory.VarChar, LengthKnown: false);
    }

    private static SqlType? ResolveReplicate(SqlType? value, ScalarExpression count)
    {
        if (value is not { IsStringFamily: true, IsLegacyLob: false })
        {
            return null;
        }

        var category = value.IsUnicodeString ? SqlTypeCategory.NVarChar : SqlTypeCategory.VarChar;
        if (value.IsMax)
        {
            return new SqlType(category, IsMax: true, Collation: value.Collation);
        }

        var cap = value.IsUnicodeString ? UnicodeCap : NonUnicodeCap;
        if (PositiveIntegerLiteral(count) is not { } times)
        {
            return new SqlType(category, Length: cap, Collation: value.Collation);
        }

        return value.Length is { } length
            ? new SqlType(category, Length: (int)Math.Min((long)length * times, cap), Collation: value.Collation)
            : new SqlType(category, LengthKnown: false, Collation: value.Collation);
    }

    private static SqlType? ResolveConcat(IReadOnlyList<SqlType?> parts, SqlType? separator)
    {
        if (parts.Any(p => p is not { IsStringFamily: true, IsLegacyLob: false })
            || separator is not null and not { IsStringFamily: true, IsLegacyLob: false })
        {
            return null;
        }

        var all = separator is null ? parts : [.. parts, separator];
        var unicode = all.Any(p => p!.IsUnicodeString);
        var category = unicode ? SqlTypeCategory.NVarChar : SqlTypeCategory.VarChar;
        if (all.Any(p => p!.IsMax))
        {
            return new SqlType(category, IsMax: true);
        }

        if (all.Any(p => p!.Length is null))
        {
            return new SqlType(category, LengthKnown: false);
        }

        var total = parts.Sum(p => (long)p!.Length!.Value) + (separator is null ? 0 : (long)separator.Length!.Value * (parts.Count - 1));
        return new SqlType(category, Length: (int)Math.Min(total, unicode ? UnicodeCap : NonUnicodeCap));
    }

    private static int? PositiveIntegerLiteral(ScalarExpression expression) =>
        expression is IntegerLiteral literal
        && int.TryParse(literal.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
        && value >= 1
            ? value
            : null;
}
