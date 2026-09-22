using System.Text.Json.Serialization;
using SilentScan.Core.Rules;

namespace SilentScan.Core.Predicates;

public enum DynamicDataMaskingFindingKind
{
    PredicateExposure,

    ComputedExpressionCollapse,
}

public sealed record DynamicDataMaskingFinding(
    string TableQualifiedName,
    string ColumnName,
    string MaskingFunctionName,
    string ContextDescription,
    [property: JsonIgnore] string SourcePath,
    [property: JsonIgnore] int Line,
    [property: JsonIgnore] int Column,
    DynamicDataMaskingFindingKind Kind,
    FindingConfidence Confidence = FindingConfidence.Medium,
    SourceSpan? DynamicSqlCallSite = null) : IRelocatableFinding<DynamicDataMaskingFinding>, IFinding
{
    public string RuleId { get; } = FindingRuleIds.DynamicDataMaskingRuleId(Kind);

    public SourceSpan Location => new(SourcePath, Line, Column);
    int IRelocatableFinding.PositionColumn => Column;

    DynamicDataMaskingFinding IRelocatableFinding<DynamicDataMaskingFinding>.Relocated(SourceSpan span, SourceSpan? callSite, FindingConfidence confidence) =>
        this with { SourcePath = span.SourcePath, Line = span.Line, Column = span.Column, DynamicSqlCallSite = callSite, Confidence = confidence };
}
