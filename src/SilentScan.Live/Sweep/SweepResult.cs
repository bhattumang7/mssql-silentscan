namespace SilentScan.Live.Sweep;

public enum SweepOutcome
{
    Confirmed,
    DeployFailed,
    OwnRuleSilent,
    OwnRuleFiredOnCompliant,
    NotSelfContained,
    ServerScopedSkipped,
    BehaviorMissing,
    BehaviorMismatch,
    HarnessError,
}

public sealed record SweepResult(
    RuleExampleCase Case,
    SweepOutcome Outcome,
    string? Detail,
    IReadOnlyList<string> ForeignRuleIdsFired);
