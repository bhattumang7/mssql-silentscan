using System.Text.Json.Serialization;
using SilentScan.Core.Rules;

namespace SilentScan.Core.Predicates;

public enum IndexCoverageFindingKind
{
    KeyLookupProneIndex,
}

public sealed record IndexCoverageFinding(
    IndexCoverageFindingKind Kind,
    string TableQualifiedName,
    string? IndexName,
    IReadOnlyList<string> IndexKeyColumns,
    IReadOnlyList<string> IndexIncludedColumns,
    IReadOnlyList<string> UncoveredColumns,
    [property: JsonIgnore] string SourcePath,
    [property: JsonIgnore] int Line,
    [property: JsonIgnore] int Column,
    FindingConfidence Confidence = FindingConfidence.High,
    SourceSpan? DynamicSqlCallSite = null) : IRelocatableFinding<IndexCoverageFinding>, IFinding
{
    public string RuleId { get; } = FindingRuleIds.IndexCoverageRuleId(Kind);

    public SourceSpan Location => new(SourcePath, Line, Column);
    int IRelocatableFinding.PositionColumn => Column;

    IndexCoverageFinding IRelocatableFinding<IndexCoverageFinding>.Relocated(SourceSpan span, SourceSpan? callSite, FindingConfidence confidence) =>
        this with { SourcePath = span.SourcePath, Line = span.Line, Column = span.Column, DynamicSqlCallSite = callSite, Confidence = confidence };
}

