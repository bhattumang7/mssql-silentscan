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
    FindingConfidence Confidence = FindingConfidence.High,
    SourceSpan? DynamicSqlCallSite = null) : IRelocatableFinding<IsNullReplacementValueTruncationFinding>, IFinding
{
    public string RuleId { get; } = FindingRuleIds.IsNullReplacementValueTruncationRuleId;

    public SourceSpan Location => new(SourcePath, Line, Column);
    int IRelocatableFinding.PositionColumn => Column;

    IsNullReplacementValueTruncationFinding IRelocatableFinding<IsNullReplacementValueTruncationFinding>.Relocated(SourceSpan span, SourceSpan? callSite, FindingConfidence confidence) =>
        this with { SourcePath = span.SourcePath, Line = span.Line, Column = span.Column, DynamicSqlCallSite = callSite, Confidence = confidence };
}
