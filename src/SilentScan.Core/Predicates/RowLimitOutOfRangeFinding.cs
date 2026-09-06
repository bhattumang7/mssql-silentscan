using System.Text.Json.Serialization;
using SilentScan.Core.Rules;

namespace SilentScan.Core.Predicates;

public enum RowLimitOutOfRangeKind
{
    TopRowCountNegative,
    OffsetNegative,
    FetchNotPositive,
    TableSamplePercentOutOfRange,
    TableSampleRowsNotPositive,
}

public sealed record RowLimitOutOfRangeFinding(
    RowLimitOutOfRangeKind Kind,
    string LiteralValueDisplay,
    [property: JsonIgnore] string SourcePath,
    [property: JsonIgnore] int Line,
    [property: JsonIgnore] int Column,
    FindingConfidence Confidence = FindingConfidence.High) : IFinding
{
    public string RuleId { get; } = FindingRuleIds.RowLimitOutOfRangeRuleId(Kind);

    public SourceSpan Location => new(SourcePath, Line, Column);
}
