using System.Text.Json.Serialization;
using SilentScan.Core.Rules;

namespace SilentScan.Core.Predicates;

public sealed record IsNullReplacementValueTruncationFinding(
    string CheckExpressionDisplay,
    string CheckExpressionTypeDisplay,
    string ReplacementValueDisplay,
    string ReplacementValueTypeDisplay,
    WriteLossKind Kind,
    [property: JsonIgnore] string SourcePath,
    [property: JsonIgnore] int Line,
    [property: JsonIgnore] int Column,
    FindingConfidence Confidence = FindingConfidence.High) : IFinding
{
    public string RuleId { get; } = FindingRuleIds.IsNullReplacementValueTruncationRuleId;

    public SourceSpan Location => new(SourcePath, Line, Column);
}
