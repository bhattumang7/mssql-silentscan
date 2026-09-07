using System.Text.Json.Serialization;
using SilentScan.Core.Rules;

namespace SilentScan.Core.Predicates;

public sealed record UnistrUnpairedSurrogateFinding(
    [property: JsonIgnore] string SourcePath,
    [property: JsonIgnore] int Line,
    [property: JsonIgnore] int Column,
    string EscapeSequence,
    FindingConfidence Confidence = FindingConfidence.High) : IFinding
{
    public string RuleId { get; } = FindingRuleIds.UnistrUnpairedSurrogateRuleId;

    public SourceSpan Location => new(SourcePath, Line, Column);
}
