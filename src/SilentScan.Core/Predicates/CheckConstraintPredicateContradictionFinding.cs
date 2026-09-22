using System.Text.Json.Serialization;
using SilentScan.Core.Rules;

namespace SilentScan.Core.Predicates;

public enum CheckConstraintPredicateContradictionKind
{
    CheckConstraintInterval,

    NotNullConstraint,
}

public sealed record CheckConstraintPredicateContradictionFinding(
    CheckConstraintPredicateContradictionKind Kind,
    string TableQualifiedName,
    string ColumnName,
    string? ConstraintName,
    [property: JsonIgnore] string SourcePath,
    [property: JsonIgnore] int Line,
    [property: JsonIgnore] int Column,
    FindingConfidence Confidence = FindingConfidence.High,
    SourceSpan? DynamicSqlCallSite = null) : IRelocatableFinding<CheckConstraintPredicateContradictionFinding>, IFinding
{
    public string RuleId { get; } = FindingRuleIds.CheckConstraintPredicateContradictionRuleId(Kind);

    public SourceSpan Location => new(SourcePath, Line, Column);
    int IRelocatableFinding.PositionColumn => Column;

    CheckConstraintPredicateContradictionFinding IRelocatableFinding<CheckConstraintPredicateContradictionFinding>.Relocated(SourceSpan span, SourceSpan? callSite, FindingConfidence confidence) =>
        this with { SourcePath = span.SourcePath, Line = span.Line, Column = span.Column, DynamicSqlCallSite = callSite, Confidence = confidence };
}
