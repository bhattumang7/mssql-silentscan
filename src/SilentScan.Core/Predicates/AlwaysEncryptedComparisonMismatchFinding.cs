using System.Text.Json.Serialization;
using SilentScan.Core.Rules;

namespace SilentScan.Core.Predicates;

public enum AlwaysEncryptedComparisonMismatchKind
{
    LiteralOperand,
    EncryptionStateMismatch,
}

public sealed record AlwaysEncryptedComparisonMismatchFinding(
    AlwaysEncryptedComparisonMismatchKind Kind,
    string FirstTableQualifiedName,
    string FirstColumnName,
    string FirstEncryptionTypeDisplay,
    string? SecondTableQualifiedName,
    string? SecondColumnName,
    string? SecondEncryptionTypeDisplay,
    [property: JsonIgnore] string SourcePath,
    [property: JsonIgnore] int Line,
    [property: JsonIgnore] int Column,
    FindingConfidence Confidence = FindingConfidence.High) : IFinding
{
    public string RuleId { get; } = FindingRuleIds.AlwaysEncryptedComparisonMismatchRuleId(Kind);

    public SourceSpan Location => new(SourcePath, Line, Column);
}
