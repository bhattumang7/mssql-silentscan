using System.Text.Json.Serialization;
using SilentScan.Core.Rules;


namespace SilentScan.Core.Predicates;

public sealed record TemporalBoundaryPrecisionFinding(
    string TableQualifiedName,
    string ColumnName,
    int ColumnScale,
    int BoundaryLiteralFractionalDigits,
    string BoundaryLiteralText,
    [property: JsonIgnore] string SourcePath,
    [property: JsonIgnore] int Line,
    [property: JsonIgnore] int Column,
    FindingConfidence Confidence = FindingConfidence.High,
    SourceSpan? DynamicSqlCallSite = null) : IRelocatableFinding<TemporalBoundaryPrecisionFinding>, IFinding
{
    public string RuleId { get; } = FindingRuleIds.TemporalBoundaryPrecisionRuleId;

    public SourceSpan Location => new(SourcePath, Line, Column);
    int IRelocatableFinding.PositionColumn => Column;

    TemporalBoundaryPrecisionFinding IRelocatableFinding<TemporalBoundaryPrecisionFinding>.Relocated(SourceSpan span, SourceSpan? callSite, FindingConfidence confidence) =>
        this with { SourcePath = span.SourcePath, Line = span.Line, Column = span.Column, DynamicSqlCallSite = callSite, Confidence = confidence };
}

