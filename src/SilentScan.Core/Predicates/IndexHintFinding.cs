using System.Text.Json.Serialization;
using SilentScan.Core.Rules;

namespace SilentScan.Core.Predicates;

public enum IndexHintFindingKind
{
    HintedIndexNotSeekable,
}

public sealed record IndexHintFinding(
    IndexHintFindingKind Kind,
    string TableQualifiedName,
    string HintedIndexName,
    string? LeadingColumnName,
    [property: JsonIgnore] string SourcePath,
    [property: JsonIgnore] int Line,
    [property: JsonIgnore] int Column,
    FindingConfidence Confidence = FindingConfidence.High,
    SourceSpan? DynamicSqlCallSite = null) : IRelocatableFinding<IndexHintFinding>, IFinding
{
    public string RuleId { get; } = FindingRuleIds.IndexHintRuleId(Kind);

    public SourceSpan Location => new(SourcePath, Line, Column);
    int IRelocatableFinding.PositionColumn => Column;

    IndexHintFinding IRelocatableFinding<IndexHintFinding>.Relocated(SourceSpan span, SourceSpan? callSite, FindingConfidence confidence) =>
        this with { SourcePath = span.SourcePath, Line = span.Line, Column = span.Column, DynamicSqlCallSite = callSite, Confidence = confidence };
}

