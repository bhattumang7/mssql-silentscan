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
    FindingConfidence Confidence = FindingConfidence.High,
    SourceSpan? DynamicSqlCallSite = null) : IRelocatableFinding<RegexpDefaultCaseSensitiveOnCiColumnFinding>, IFinding
{
    public string RuleId { get; } = FindingRuleIds.RegexpDefaultCaseSensitiveOnCiColumnRuleId;

    public SourceSpan Location => new(SourcePath, Line, Column);
    int IRelocatableFinding.PositionColumn => Column;

    RegexpDefaultCaseSensitiveOnCiColumnFinding IRelocatableFinding<RegexpDefaultCaseSensitiveOnCiColumnFinding>.Relocated(SourceSpan span, SourceSpan? callSite, FindingConfidence confidence) =>
        this with { SourcePath = span.SourcePath, Line = span.Line, Column = span.Column, DynamicSqlCallSite = callSite, Confidence = confidence };
}
