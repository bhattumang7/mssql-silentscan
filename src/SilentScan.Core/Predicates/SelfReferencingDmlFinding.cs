using System.Text.Json.Serialization;
using SilentScan.Core.Rules;

namespace SilentScan.Core.Predicates;

public enum SelfReferencingDmlFindingKind
{
    DirectTableReference,
    ThroughView,
}

public sealed record SelfReferencingDmlFinding(
    SelfReferencingDmlFindingKind Kind,
    string StatementKind,
    string TargetTableQualifiedName,
    string ReadSideQualifiedName,
    [property: JsonIgnore] string SourcePath,
    [property: JsonIgnore] int Line,
    [property: JsonIgnore] int Column,
    FindingConfidence Confidence = FindingConfidence.High,
    SourceSpan? DynamicSqlCallSite = null) : IRelocatableFinding<SelfReferencingDmlFinding>, IFinding
{
    public string RuleId { get; } = FindingRuleIds.SelfReferencingDmlRuleId;

    public SourceSpan Location => new(SourcePath, Line, Column);
    int IRelocatableFinding.PositionColumn => Column;

    SelfReferencingDmlFinding IRelocatableFinding<SelfReferencingDmlFinding>.Relocated(SourceSpan span, SourceSpan? callSite, FindingConfidence confidence) =>
        this with { SourcePath = span.SourcePath, Line = span.Line, Column = span.Column, DynamicSqlCallSite = callSite, Confidence = confidence };
}
