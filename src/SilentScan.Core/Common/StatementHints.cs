using System.Globalization;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SilentScan.Core.Common;

internal static class StatementHints
{
    private const string CompatibilityLevelPrefix = "QUERY_OPTIMIZER_COMPATIBILITY_LEVEL_";

    public static bool UsesHint(IList<OptimizerHint> hints, string hintName) =>
        UseHints(hints).Any(hint => string.Equals(hint, hintName, StringComparison.OrdinalIgnoreCase));

    public static bool DisablesParameterSniffing(IList<OptimizerHint> hints) =>
        UsesHint(hints, "DISABLE_PARAMETER_SNIFFING")
        || hints.OfType<OptimizeForOptimizerHint>().Any(hint => hint.IsForUnknown);

    public static bool OptimizesForUnknown(IList<OptimizerHint> hints, string variableName) =>
        DisablesParameterSniffing(hints)
        || hints.OfType<OptimizeForOptimizerHint>()
            .SelectMany(hint => hint.Pairs)
            .Any(pair => pair.Value is null && string.Equals(pair.Variable.Name, variableName, StringComparison.OrdinalIgnoreCase));

    public static int? CompatibilityLevelOverride(IList<OptimizerHint> hints) =>
        UseHints(hints)
            .Select(ParseCompatibilityLevel)
            .FirstOrDefault(level => level is not null);

    private static IEnumerable<string> UseHints(IList<OptimizerHint> hints) =>
        hints.OfType<UseHintList>().SelectMany(list => list.Hints).Select(hint => hint.Value);

    private static int? ParseCompatibilityLevel(string hint) =>
        hint.StartsWith(CompatibilityLevelPrefix, StringComparison.OrdinalIgnoreCase)
        && int.TryParse(hint.AsSpan(CompatibilityLevelPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var level)
            ? level
            : null;
}
