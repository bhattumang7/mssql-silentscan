using System.Text.Json.Serialization;
using SilentScan.Core.Rules;

namespace SilentScan.Core.Predicates;

public sealed record ParameterReassignmentPredicateFinding(
    string TableQualifiedName,
    string ColumnName,
    bool Indexed,
    string ParameterName,
    string Operator,
    int ReassignmentLine,
    int ReassignmentColumn,
    [property: JsonIgnore] string SourcePath,
    [property: JsonIgnore] int Line,
    [property: JsonIgnore] int Column,
    FindingConfidence Confidence = FindingConfidence.Low,
    SourceSpan? DynamicSqlCallSite = null) : IRelocatableFinding<ParameterReassignmentPredicateFinding>, IFinding
{
    public string RuleId { get; } = FindingRuleIds.ParameterReassignmentPredicateRuleId;

    public SourceSpan Location => new(SourcePath, Line, Column);
    int IRelocatableFinding.PositionColumn => Column;

    ParameterReassignmentPredicateFinding IRelocatableFinding<ParameterReassignmentPredicateFinding>.Relocated(SourceSpan span, SourceSpan? callSite, FindingConfidence confidence) =>
        this with { SourcePath = span.SourcePath, Line = span.Line, Column = span.Column, DynamicSqlCallSite = callSite, Confidence = confidence };
}

