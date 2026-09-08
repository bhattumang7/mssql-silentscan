namespace SilentScan.Live.Sweep;

public enum RuleExampleVariant
{
    Noncompliant,
    Compliant,
}

public sealed record RuleExampleCase(
    string RuleId,
    int ExampleIndex,
    string Title,
    RuleExampleVariant Variant,
    string DeployableSql,
    bool IsSelfContained);
