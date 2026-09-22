namespace SilentScan.Core.Reporting;

public static class StyleRuleFamilies
{
    private static readonly string[] Prefixes =
    [
        "silentscan/formatting/",
        "silentscan/metrics/",
        "silentscan/dead-code/",
        "silentscan/duplication/",
        "silentscan/deprecated-syntax/task-comment-",
        "silentscan/control-flow/goto-usage",
    ];

    public static bool IsStyleRule(string ruleId) =>
        Prefixes.Any(prefix => ruleId.StartsWith(prefix, StringComparison.Ordinal));
}
