using SilentScan.Core.Reporting;

namespace SilentScan.Tests.Support;

public static class RuleConditionCoverage
{
    public static List<string> Problems(
        IReadOnlyList<RuleCondition> conditions, IReadOnlyList<OracleCase> firing, IReadOnlyList<SilentCase> silent)
    {
        var problems = new List<string>();
        if (conditions.Count == 0)
        {
            problems.Add("the rule declares no firing conditions or exclusions in the catalog");
            return problems;
        }

        var firingEntries = firing.Select(c => c.Entry).ToHashSet(StringComparer.Ordinal);
        var silentEntries = silent.Select(c => c.Case.Entry).ToHashSet(StringComparer.Ordinal);

        problems.AddRange(conditions
            .Where(c => c.Kind == RuleConditionKind.Fires && !firingEntries.Contains(c.Name))
            .Select(c => $"firing condition '{c.Name}' has no firing case"));
        problems.AddRange(conditions
            .Where(c => c.Kind == RuleConditionKind.Excluded && !silentEntries.Contains(c.Name))
            .Select(c => $"exclusion '{c.Name}' has no silent case"));

        var fires = conditions.Where(c => c.Kind == RuleConditionKind.Fires).Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
        var exclusions = conditions.Where(c => c.Kind == RuleConditionKind.Excluded).Select(c => c.Name).ToHashSet(StringComparer.Ordinal);

        problems.AddRange(firing
            .Where(c => !fires.Contains(c.Entry))
            .Select(c => $"firing case '{c.Name}' names '{c.Entry}', which is not a firing condition of the rule"));
        problems.AddRange(silent
            .Where(c => !exclusions.Contains(c.Case.Entry))
            .Select(c => $"silent case '{c.Case.Name}' names '{c.Case.Entry}', which is not an exclusion of the rule"));

        return problems;
    }
}
