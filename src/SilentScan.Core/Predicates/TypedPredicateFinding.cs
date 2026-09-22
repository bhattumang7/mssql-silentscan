using SilentScan.Core.Diagnostics;
using System.Text.Json.Serialization;
using SilentScan.Core.Rules;


namespace SilentScan.Core.Predicates;

public sealed record TypedPredicateFinding(
    Verdict Verdict,
    PredicateOperand.Column Column,
    PredicateOperand OtherOperand,
    string Operator,
    [property: JsonIgnore] string SourcePath,
    [property: JsonIgnore] int Line,
    [property: JsonIgnore] int ColumnPosition,
    SourceSpan? DynamicSqlCallSite = null,
    string? UnknownReason = null,
    string? PredicateFragmentText = null,
    string? Fingerprint = null,
    FindingConfidence Confidence = FindingConfidence.High) : IRelocatableFinding<TypedPredicateFinding>, IFinding
{
    public string? RuleId { get; } = Verdict is Verdict.Unknown or Verdict.OperandClash ? null : FindingRuleIds.VerdictRuleId(Verdict);

    public SourceSpan Location => new(SourcePath, Line, ColumnPosition);
    int IRelocatableFinding.PositionColumn => ColumnPosition;

    TypedPredicateFinding IRelocatableFinding<TypedPredicateFinding>.Relocated(SourceSpan span, SourceSpan? callSite, FindingConfidence confidence) =>
        this with { SourcePath = span.SourcePath, Line = span.Line, ColumnPosition = span.Column, DynamicSqlCallSite = callSite, Confidence = confidence };
}

public sealed record PredicateExtractionResult(
    IReadOnlyList<TypedPredicateFinding> TypedFindings,
    IReadOnlyList<ExpressionDerivedFinding> ExpressionDerivedFindings,
    IReadOnlyList<WriteLossFinding> WriteLossFindings,
    IReadOnlyList<SkippedConstruct> SkippedConstructs,
    IReadOnlyList<UnderLengthParameterFinding> UnderLengthParameterFindings,
    IReadOnlyList<AnsiPaddingMismatchFinding> AnsiPaddingMismatchFindings,
    IReadOnlyList<LocalVariablePredicateFinding> LocalVariablePredicateFindings,
    IReadOnlyList<FilteredIndexParameterMismatchFinding> FilteredIndexParameterMismatchFindings);
