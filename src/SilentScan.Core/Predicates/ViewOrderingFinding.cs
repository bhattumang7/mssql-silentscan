using System.Text.Json.Serialization;
using SilentScan.Core.Rules;


namespace SilentScan.Core.Predicates;

public enum ViewOrderingFindingKind
{
    TopPercentOrderByNeverLimits,

    OrderByNotGuaranteedToConsumer,
}

public sealed record ViewOrderingFinding(
    ViewOrderingFindingKind Kind,
    string ObjectQualifiedName,
    [property: JsonIgnore] string SourcePath,
    [property: JsonIgnore] int Line,
    [property: JsonIgnore] int Column,
    FindingConfidence Confidence,
    SourceSpan? DynamicSqlCallSite = null) : IRelocatableFinding<ViewOrderingFinding>, IFinding
{
    public string RuleId { get; } = FindingRuleIds.ViewOrderingRuleId(Kind);

    public SourceSpan Location => new(SourcePath, Line, Column);
    int IRelocatableFinding.PositionColumn => Column;

    ViewOrderingFinding IRelocatableFinding<ViewOrderingFinding>.Relocated(SourceSpan span, SourceSpan? callSite, FindingConfidence confidence) =>
        this with { SourcePath = span.SourcePath, Line = span.Line, Column = span.Column, DynamicSqlCallSite = callSite, Confidence = confidence };
}

