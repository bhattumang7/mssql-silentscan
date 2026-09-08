namespace SilentScan.Live.Sweep;

public enum MetamorphicOutcome
{
    Matched,
    Mismatched,
}

public sealed record MetamorphicResult(
    RuleExampleCase BaseCase,
    string MutationName,
    MetamorphicOutcome Outcome,
    IReadOnlySet<string> BaselineFiredRuleIds,
    IReadOnlySet<string> MutatedFiredRuleIds);
