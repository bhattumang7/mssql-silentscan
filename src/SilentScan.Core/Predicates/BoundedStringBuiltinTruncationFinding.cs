using System.Text.Json.Serialization;
using SilentScan.Core.Rules;

namespace SilentScan.Core.Predicates;

public enum BoundedStringBuiltinTruncationFindingKind
{
    ReplicateResultTruncated,
    ReplaceResultTruncated,
    SpaceResultTruncated,
}

public sealed record BoundedStringBuiltinTruncationFinding(
    BoundedStringBuiltinTruncationFindingKind Kind,
    string FunctionName,
    long ComputedLength,
    int CapBytes,
    [property: JsonIgnore] string SourcePath,
    [property: JsonIgnore] int Line,
    [property: JsonIgnore] int Column,
    FindingConfidence Confidence = FindingConfidence.High,
    SourceSpan? DynamicSqlCallSite = null) : IRelocatableFinding<BoundedStringBuiltinTruncationFinding>, IFinding
{
    public string RuleId { get; } = FindingRuleIds.BoundedStringBuiltinTruncationRuleId(Kind);

    public SourceSpan Location => new(SourcePath, Line, Column);
    int IRelocatableFinding.PositionColumn => Column;

    BoundedStringBuiltinTruncationFinding IRelocatableFinding<BoundedStringBuiltinTruncationFinding>.Relocated(SourceSpan span, SourceSpan? callSite, FindingConfidence confidence) =>
        this with { SourcePath = span.SourcePath, Line = span.Line, Column = span.Column, DynamicSqlCallSite = callSite, Confidence = confidence };
}
