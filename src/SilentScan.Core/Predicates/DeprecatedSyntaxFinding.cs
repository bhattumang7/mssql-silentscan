using System.Text.Json.Serialization;
using SilentScan.Core.Rules;

namespace SilentScan.Core.Predicates;

public enum DeprecatedSyntaxFindingKind
{
    TaskCommentTodo,

    TaskCommentFixme,


    EqualsNullComparison,

    NotEqualsNullComparison,


    LegacySystemCompatibilityView,

    TableHintWithoutWith,

    NumberedProcedureDefinition,

    NumberedProcedureExecution,



    DeprecatedSetRowcount,

    LegacyLobStatement,

    LegacyLobFunction,
}

public sealed record DeprecatedSyntaxFinding(
    DeprecatedSyntaxFindingKind Kind,
    string ModuleQualifiedName,
    [property: JsonIgnore] string SourcePath,
    [property: JsonIgnore] int Line,
    [property: JsonIgnore] int Column,
    string DetailText,
    FindingConfidence Confidence = FindingConfidence.Medium,
    SourceSpan? DynamicSqlCallSite = null) : IRelocatableFinding<DeprecatedSyntaxFinding>, IFinding
{
    public string RuleId { get; } = FindingRuleIds.DeprecatedSyntaxRuleId(Kind);

    public SourceSpan Location => new(SourcePath, Line, Column);
    int IRelocatableFinding.PositionColumn => Column;

    DeprecatedSyntaxFinding IRelocatableFinding<DeprecatedSyntaxFinding>.Relocated(SourceSpan span, SourceSpan? callSite, FindingConfidence confidence) =>
        this with { SourcePath = span.SourcePath, Line = span.Line, Column = span.Column, DynamicSqlCallSite = callSite, Confidence = confidence };
}

