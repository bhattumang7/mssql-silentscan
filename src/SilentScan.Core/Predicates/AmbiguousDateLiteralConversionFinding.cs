using System.Text.Json.Serialization;
using SilentScan.Core.Rules;

namespace SilentScan.Core.Predicates;

public sealed record AmbiguousDateLiteralConversionFinding(
    string LiteralText,
    [property: JsonIgnore] string SourcePath,
    [property: JsonIgnore] int Line,
    [property: JsonIgnore] int Column,
    FindingConfidence Confidence = FindingConfidence.Low,
    SourceSpan? DynamicSqlCallSite = null) : IRelocatableFinding<AmbiguousDateLiteralConversionFinding>, IFinding
{
    public string RuleId { get; } = FindingRuleIds.AmbiguousDateLiteralConversionRuleId;

    public SourceSpan Location => new(SourcePath, Line, Column);
    int IRelocatableFinding.PositionColumn => Column;

    AmbiguousDateLiteralConversionFinding IRelocatableFinding<AmbiguousDateLiteralConversionFinding>.Relocated(SourceSpan span, SourceSpan? callSite, FindingConfidence confidence) =>
        this with { SourcePath = span.SourcePath, Line = span.Line, Column = span.Column, DynamicSqlCallSite = callSite, Confidence = confidence };
}
