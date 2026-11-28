using System.Globalization;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SilentScan.Core.Common;

internal static class StatementHints
{
    private const string CompatibilityLevelPrefix = "QUERY_OPTIMIZER_COMPATIBILITY_LEVEL_";

    public static bool HasRecompile(StatementWithCtesAndXmlNamespaces statement) =>
        statement.OptimizerHints.Any(h => h.HintKind == OptimizerHintKind.Recompile);

    public static bool UsesHint(StatementWithCtesAndXmlNamespaces statement, string hintName) =>
        statement.OptimizerHints.OfType<UseHintList>()
            .SelectMany(list => list.Hints)
            .Any(hint => string.Equals(hint.Value, hintName, StringComparison.OrdinalIgnoreCase));

    public static bool DisablesDeferredTableVariableCompilation(StatementWithCtesAndXmlNamespaces statement) =>
        UsesHint(statement, "DISABLE_DEFERRED_COMPILATION_TV");

    public static bool DisablesParameterSniffing(StatementWithCtesAndXmlNamespaces statement) =>
        UsesHint(statement, "DISABLE_PARAMETER_SNIFFING")
        || statement.OptimizerHints.OfType<OptimizeForOptimizerHint>().Any(hint => hint.IsForUnknown);

    public static bool OptimizesForUnknown(StatementWithCtesAndXmlNamespaces statement, string variableName) =>
        DisablesParameterSniffing(statement) || OptimizeForPairs(statement, variableName).Any(pair => pair.Value is null);

    public static bool IgnoresRuntimeValue(StatementWithCtesAndXmlNamespaces statement, string variableName) =>
        DisablesParameterSniffing(statement) || OptimizeForPairs(statement, variableName).Any();

    private static IEnumerable<VariableValuePair> OptimizeForPairs(StatementWithCtesAndXmlNamespaces statement, string variableName) =>
        statement.OptimizerHints.OfType<OptimizeForOptimizerHint>()
            .SelectMany(hint => hint.Pairs)
            .Where(pair => string.Equals(pair.Variable.Name, variableName, StringComparison.OrdinalIgnoreCase));

    public static int? CompatibilityLevelOverride(StatementWithCtesAndXmlNamespaces statement) =>
        statement.OptimizerHints.OfType<UseHintList>()
            .SelectMany(list => list.Hints)
            .Select(hint => hint.Value)
            .Select(ParseCompatibilityLevel)
            .FirstOrDefault(level => level is not null);

    private static int? ParseCompatibilityLevel(string hint) =>
        hint.StartsWith(CompatibilityLevelPrefix, StringComparison.OrdinalIgnoreCase)
        && int.TryParse(hint.AsSpan(CompatibilityLevelPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var level)
            ? level
            : null;
}
