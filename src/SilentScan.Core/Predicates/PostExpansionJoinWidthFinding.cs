using System.Text.Json.Serialization;
using SilentScan.Core.Rules;

namespace SilentScan.Core.Predicates;

public sealed record PostExpansionJoinWidthFinding(
    string ModuleQualifiedName,
    int WrittenCount,
    int ExpandedCount,
    IReadOnlyList<string> ExpandedBaseTables,
    IReadOnlyList<string> InflatingSources,
    bool PartiallyUnexpanded,
    [property: JsonIgnore] string SourcePath,
    [property: JsonIgnore] int Line,
    [property: JsonIgnore] int Column,
    FindingConfidence Confidence = FindingConfidence.High,
    SourceSpan? DynamicSqlCallSite = null) : IRelocatableFinding<PostExpansionJoinWidthFinding>, IFinding
{
    public string RuleId { get; } = FindingRuleIds.PostExpansionJoinWidthRuleId;

    public SourceSpan Location => new(SourcePath, Line, Column);
    int IRelocatableFinding.PositionColumn => Column;

    PostExpansionJoinWidthFinding IRelocatableFinding<PostExpansionJoinWidthFinding>.Relocated(SourceSpan span, SourceSpan? callSite, FindingConfidence confidence) =>
        this with { SourcePath = span.SourcePath, Line = span.Line, Column = span.Column, DynamicSqlCallSite = callSite, Confidence = confidence };
}

