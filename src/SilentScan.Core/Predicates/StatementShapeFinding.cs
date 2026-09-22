using System.Text.Json.Serialization;
using SilentScan.Core.Rules;


namespace SilentScan.Core.Predicates;

public enum StatementShapeFindingKind
{
    InsertWithoutColumnList,

    OrdinalOrderBy,

    TableWithNoPrimaryKey,

    MissingSetNocountOn,

    BareSelectStar,
}

public sealed record StatementShapeFinding(
    StatementShapeFindingKind Kind,
    string ModuleQualifiedName,
    [property: JsonIgnore] string SourcePath,
    [property: JsonIgnore] int Line,
    [property: JsonIgnore] int Column,
    string DetailText,
    FindingConfidence Confidence = FindingConfidence.Medium,
    SourceSpan? DynamicSqlCallSite = null) : IRelocatableFinding<StatementShapeFinding>, IFinding
{
    public string RuleId { get; } = FindingRuleIds.StatementShapeRuleId(Kind);

    public SourceSpan Location => new(SourcePath, Line, Column);
    int IRelocatableFinding.PositionColumn => Column;

    StatementShapeFinding IRelocatableFinding<StatementShapeFinding>.Relocated(SourceSpan span, SourceSpan? callSite, FindingConfidence confidence) =>
        this with { SourcePath = span.SourcePath, Line = span.Line, Column = span.Column, DynamicSqlCallSite = callSite, Confidence = confidence };
}

