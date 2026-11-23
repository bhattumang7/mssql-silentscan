using System.Text.Json.Serialization;
using SilentScan.Core.Rules;

namespace SilentScan.Core.Predicates;

public sealed record ExactApproximateComparisonFinding(
    string ExactColumn,
    string ExactTypeDisplay,
    string ApproximateColumn,
    string ApproximateTypeDisplay,
    [property: JsonIgnore] string SourcePath,
    [property: JsonIgnore] int Line,
    [property: JsonIgnore] int Column,
    FindingConfidence Confidence = FindingConfidence.High,
    SourceSpan? DynamicSqlCallSite = null) : IRelocatableFinding<ExactApproximateComparisonFinding>, IFinding
{
    public string RuleId { get; } = FindingRuleIds.ExactApproximateComparisonRuleId;

    public SourceSpan Location => new(SourcePath, Line, Column);
    int IRelocatableFinding.PositionColumn => Column;

    ExactApproximateComparisonFinding IRelocatableFinding<ExactApproximateComparisonFinding>.Relocated(SourceSpan span, SourceSpan? callSite, FindingConfidence confidence) =>
        this with { SourcePath = span.SourcePath, Line = span.Line, Column = span.Column, DynamicSqlCallSite = callSite, Confidence = confidence };
}
