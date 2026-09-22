using System.Text.Json.Serialization;
using SilentScan.Core.Rules;

namespace SilentScan.Core.Predicates;

public enum OuterJoinPredicateCollapseKind
{
    LeftOuterJoin,
    RightOuterJoin,
    FullOuterJoin,
}

public sealed record OuterJoinPredicateCollapseFinding(
    OuterJoinPredicateCollapseKind Kind,
    string NullSupplyingTableQualifiedName,
    string ColumnName,
    [property: JsonIgnore] string SourcePath,
    [property: JsonIgnore] int Line,
    [property: JsonIgnore] int Column,
    FindingConfidence Confidence = FindingConfidence.High,
    SourceSpan? DynamicSqlCallSite = null) : IRelocatableFinding<OuterJoinPredicateCollapseFinding>, IFinding
{
    public string RuleId { get; } = FindingRuleIds.OuterJoinPredicateCollapseRuleId;

    public SourceSpan Location => new(SourcePath, Line, Column);
    int IRelocatableFinding.PositionColumn => Column;

    OuterJoinPredicateCollapseFinding IRelocatableFinding<OuterJoinPredicateCollapseFinding>.Relocated(SourceSpan span, SourceSpan? callSite, FindingConfidence confidence) =>
        this with { SourcePath = span.SourcePath, Line = span.Line, Column = span.Column, DynamicSqlCallSite = callSite, Confidence = confidence };
}
