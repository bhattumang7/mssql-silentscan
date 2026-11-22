using SilentScan.Core.Rules;
namespace SilentScan.Core.Predicates;

public enum DatabaseConfigurationFindingKind
{
    AutoShrinkOn,

    AutoCloseOn,

    AutoCreateStatisticsOff,

    AutoUpdateStatisticsOff,

    SpatialPersistedComputedColumnDisabledOnCompatibilityLevelChange,
}

public sealed record DatabaseConfigurationFinding(
    DatabaseConfigurationFindingKind Kind,
    string DatabaseName,
    FindingConfidence Confidence = FindingConfidence.High,
    string? AffectedObjectName = null,
    string? Dependency = null,
    int? TargetCompatibilityLevel = null) : IFinding
{
    public string RuleId { get; } = FindingRuleIds.DatabaseConfigurationRuleId(Kind);

    public SourceSpan Location => new(DatabaseName, 0, 0);
}

