using System.Text.Json.Serialization;
using SilentScan.Core.Common;
using SilentScan.Core.Rules;

namespace SilentScan.Core.Predicates;

public enum QueryAntiPatternFindingKind
{
    TableVariablePspSkip,

    TableVariableLowCompatEstimate,

    TableVariableStaleEstimateInLoop,

    RbarSingleRowLoopDml,

    GlobalCursorDeclaration,

    CountStarVariableExistenceCheck,

    NonAggregateHavingPredicate,


    DistinctMaskingJoinFanout,

    UnqualifiedTableReference,

    MergeMissingHoldlock,

    MergeUnconditionalDelete,


    UnboundedTableWrite,


    MultiRowInsertIgnoreDupKeyDrop,
}

public sealed record QueryAntiPatternFinding(
    QueryAntiPatternFindingKind Kind,
    [property: JsonIgnore] string SourcePath,
    [property: JsonIgnore] int Line,
    [property: JsonIgnore] int Column,
    string DetailText,
    FindingConfidence Confidence = FindingConfidence.Medium,
    SourceSpan? DynamicSqlCallSite = null) : IRelocatableFinding<QueryAntiPatternFinding>, IFinding
{
    public string RuleId { get; } = FindingRuleIds.QueryAntiPatternRuleId(Kind);

    public SourceSpan Location => new(SourcePath, Line, Column);
    int IRelocatableFinding.PositionColumn => Column;

    QueryAntiPatternFinding IRelocatableFinding<QueryAntiPatternFinding>.Relocated(SourceSpan span, SourceSpan? callSite, FindingConfidence confidence) =>
        this with { SourcePath = span.SourcePath, Line = span.Line, Column = span.Column, DynamicSqlCallSite = callSite, Confidence = confidence };
}
