using System.Text.Json.Serialization;
using SilentScan.Core.Rules;

namespace SilentScan.Core.Predicates;

public sealed record TvfCallArgumentMismatchFinding(
    string? CallerScopeQualifiedName,
    string CalleeQualifiedName,
    string FormalParameterName,
    string CallerExpressionDisplay,
    string CallerTypeDisplay,
    string FormalParameterTypeDisplay,
    WriteLossKind Kind,
    [property: JsonIgnore] string SourcePath,
    [property: JsonIgnore] int Line,
    [property: JsonIgnore] int Column,
    FindingConfidence Confidence = FindingConfidence.High,
    SourceSpan? DynamicSqlCallSite = null) : IRelocatableFinding<TvfCallArgumentMismatchFinding>, IFinding
{
    public string RuleId { get; } = FindingRuleIds.TvfCallArgumentMismatchRuleId;

    public SourceSpan Location => new(SourcePath, Line, Column);
    int IRelocatableFinding.PositionColumn => Column;

    TvfCallArgumentMismatchFinding IRelocatableFinding<TvfCallArgumentMismatchFinding>.Relocated(SourceSpan span, SourceSpan? callSite, FindingConfidence confidence) =>
        this with { SourcePath = span.SourcePath, Line = span.Line, Column = span.Column, DynamicSqlCallSite = callSite, Confidence = confidence };
}
