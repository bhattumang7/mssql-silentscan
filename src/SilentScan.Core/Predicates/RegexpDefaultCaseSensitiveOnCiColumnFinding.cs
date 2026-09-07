using System.Text.Json.Serialization;
using SilentScan.Core.Rules;

namespace SilentScan.Core.Predicates;

public sealed record RegexpDefaultCaseSensitiveOnCiColumnFinding(
    string FunctionName,
    string TableQualifiedName,
    string ColumnName,
    [property: JsonIgnore] string SourcePath,
    [property: JsonIgnore] int Line,
    [property: JsonIgnore] int Column,
    FindingConfidence Confidence = FindingConfidence.High) : IFinding
{
    public string RuleId { get; } = FindingRuleIds.RegexpDefaultCaseSensitiveOnCiColumnRuleId;

    public SourceSpan Location => new(SourcePath, Line, Column);
}
