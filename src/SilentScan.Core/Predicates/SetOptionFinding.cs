using System.Text.Json.Serialization;
using SilentScan.Core.Rules;


namespace SilentScan.Core.Predicates;

public enum SetOptionFindingKind
{
    QuotedIdentifierOffBlocksIndexedFeature,

    NumericRoundabortOnBlocksIndexedFeature,

    AnsiNullsOffBlocksIndexedFeature,

    AnsiWarningsOffBlocksIndexedFeature,

    ConcatNullYieldsNullOffBlocksIndexedFeature,

    AnsiPaddingOffBlocksIndexedFeature,
}

public sealed record SetOptionFinding(
    SetOptionFindingKind Kind,
    string ModuleQualifiedName,
    [property: JsonIgnore] string SourcePath,
    [property: JsonIgnore] int Line,
    [property: JsonIgnore] int Column,
    string? TouchedObjectQualifiedName = null,
    string? TouchedIndexName = null,
    bool TouchedIsIndexedView = false,
    FindingConfidence Confidence = FindingConfidence.High,
    SourceSpan? DynamicSqlCallSite = null) : IRelocatableFinding<SetOptionFinding>, IFinding
{
    public string RuleId { get; } = FindingRuleIds.SetOptionRuleId(Kind);

    public SourceSpan Location => new(SourcePath, Line, Column);
    int IRelocatableFinding.PositionColumn => Column;

    SetOptionFinding IRelocatableFinding<SetOptionFinding>.Relocated(SourceSpan span, SourceSpan? callSite, FindingConfidence confidence) =>
        this with { SourcePath = span.SourcePath, Line = span.Line, Column = span.Column, DynamicSqlCallSite = callSite, Confidence = confidence };
}

