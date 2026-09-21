using System.Text.Json.Serialization;
using SilentScan.Core.Rules;

namespace SilentScan.Core.Predicates;

public enum DeprecatedSyntaxFindingKind
{
    TaskCommentTodo,

    TaskCommentFixme,

    NonAnsiComparisonOperator,

    EqualsNullComparison,

    NotEqualsNullComparison,


    LegacySystemCompatibilityView,

    TableHintWithoutWith,

    NumberedProcedureDefinition,

    NumberedProcedureExecution,


    RemovedSecurityStoredProcedure,

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
    FindingConfidence Confidence = FindingConfidence.Medium) : IFinding
{
    public string RuleId { get; } = FindingRuleIds.DeprecatedSyntaxRuleId(Kind);

    public SourceSpan Location => new(SourcePath, Line, Column);
}

