using System.Text.Json.Serialization;
using SilentScan.Core.Rules;

namespace SilentScan.Core.Predicates;

public sealed record FloatOrderDependentAggregateFinding(
    string TableQualifiedName,
    string ColumnName,
    string TypeDisplay,
    string AggregateFunctionName,
    [property: JsonIgnore] string SourcePath,
    [property: JsonIgnore] int Line,
    [property: JsonIgnore] int Column,
    FindingConfidence Confidence = FindingConfidence.High,
    SourceSpan? DynamicSqlCallSite = null) : IRelocatableFinding<FloatOrderDependentAggregateFinding>, IFinding
{
    public string RuleId { get; } = FindingRuleIds.FloatOrderDependentAggregateRuleId;

    public SourceSpan Location => new(SourcePath, Line, Column);
    int IRelocatableFinding.PositionColumn => Column;

    FloatOrderDependentAggregateFinding IRelocatableFinding<FloatOrderDependentAggregateFinding>.Relocated(SourceSpan span, SourceSpan? callSite, FindingConfidence confidence) =>
        this with { SourcePath = span.SourcePath, Line = span.Line, Column = span.Column, DynamicSqlCallSite = callSite, Confidence = confidence };
}
