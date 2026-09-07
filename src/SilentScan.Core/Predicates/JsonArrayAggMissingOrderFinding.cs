using System.Text.Json.Serialization;
using SilentScan.Core.Rules;

namespace SilentScan.Core.Predicates;

public sealed record JsonArrayAggMissingOrderFinding(
    [property: JsonIgnore] string SourcePath,
    [property: JsonIgnore] int Line,
    [property: JsonIgnore] int Column,
    FindingConfidence Confidence = FindingConfidence.Medium) : IFinding
{
    public string RuleId { get; } = FindingRuleIds.JsonArrayAggMissingOrderRuleId;

    public SourceSpan Location => new(SourcePath, Line, Column);
}
