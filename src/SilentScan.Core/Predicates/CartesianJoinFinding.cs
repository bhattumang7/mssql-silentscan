using System.Text.Json.Serialization;
using SilentScan.Core.Rules;

namespace SilentScan.Core.Predicates;

public enum CartesianJoinKind
{
    CommaJoin,
    ExplicitCrossJoin,
    AlwaysFalseInnerJoinPredicate,
    JoinPredicateEmptyWithWhereClause,
}

public sealed record CartesianJoinFinding(
    CartesianJoinKind Kind,
    string FirstTableQualifiedName,
    string SecondTableQualifiedName,
    [property: JsonIgnore] string SourcePath,
    [property: JsonIgnore] int Line,
    [property: JsonIgnore] int Column,
    FindingConfidence Confidence = FindingConfidence.High,
    SourceSpan? DynamicSqlCallSite = null) : IRelocatableFinding<CartesianJoinFinding>, IFinding
{
    public string RuleId { get; } = FindingRuleIds.CartesianJoinRuleId(Kind);

    public SourceSpan Location => new(SourcePath, Line, Column);
    int IRelocatableFinding.PositionColumn => Column;

    CartesianJoinFinding IRelocatableFinding<CartesianJoinFinding>.Relocated(SourceSpan span, SourceSpan? callSite, FindingConfidence confidence) =>
        this with { SourcePath = span.SourcePath, Line = span.Line, Column = span.Column, DynamicSqlCallSite = callSite, Confidence = confidence };
}

