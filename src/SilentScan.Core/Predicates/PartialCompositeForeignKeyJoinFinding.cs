using System.Text.Json.Serialization;
using SilentScan.Core.Rules;

namespace SilentScan.Core.Predicates;

public sealed record ForeignKeyColumnPair(string ParentColumnName, string ReferencedColumnName);

public sealed record PartialCompositeForeignKeyJoinFinding(
    string ConstraintName,
    string ParentTableQualifiedName,
    string ReferencedTableQualifiedName,
    IReadOnlyList<ForeignKeyColumnPair> AllColumnPairs,
    IReadOnlyList<ForeignKeyColumnPair> MatchedColumnPairs,
    IReadOnlyList<ForeignKeyColumnPair> MissingColumnPairs,
    [property: JsonIgnore] string SourcePath,
    [property: JsonIgnore] int Line,
    [property: JsonIgnore] int Column,
    FindingConfidence Confidence = FindingConfidence.Medium,
    SourceSpan? DynamicSqlCallSite = null) : IRelocatableFinding<PartialCompositeForeignKeyJoinFinding>, IFinding
{
    public string RuleId { get; } = FindingRuleIds.PartialCompositeForeignKeyJoinRuleId;

    public SourceSpan Location => new(SourcePath, Line, Column);
    int IRelocatableFinding.PositionColumn => Column;

    PartialCompositeForeignKeyJoinFinding IRelocatableFinding<PartialCompositeForeignKeyJoinFinding>.Relocated(SourceSpan span, SourceSpan? callSite, FindingConfidence confidence) =>
        this with { SourcePath = span.SourcePath, Line = span.Line, Column = span.Column, DynamicSqlCallSite = callSite, Confidence = confidence };
}
