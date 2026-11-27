using System.Globalization;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SilentScan.Core.Common;

internal static class StatementHints
{
    private const string CompatibilityLevelPrefix = "QUERY_OPTIMIZER_COMPATIBILITY_LEVEL_";

    public static bool HasRecompile(StatementWithCtesAndXmlNamespaces statement) =>
        statement.OptimizerHints.Any(h => h.HintKind == OptimizerHintKind.Recompile);

    public static int? CompatibilityLevelOverride(StatementWithCtesAndXmlNamespaces statement)
    {
        foreach (var hint in statement.OptimizerHints.OfType<UseHintList>().SelectMany(list => list.Hints))
        {
            if (hint.Value.StartsWith(CompatibilityLevelPrefix, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(hint.Value.AsSpan(CompatibilityLevelPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var level))
            {
                return level;
            }
        }

        return null;
    }
}
