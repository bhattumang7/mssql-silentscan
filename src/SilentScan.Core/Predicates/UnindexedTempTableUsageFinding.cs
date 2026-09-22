
using SilentScan.Core.Rules;
namespace SilentScan.Core.Predicates;

public enum UnindexedTempTableUsageKind
{
    JoinOperand,
    FilteredInWhere,
}

public sealed record UnindexedTempTableUsageFinding(
    UnindexedTempTableUsageKind Kind,
    string TempTableQualifiedName,
    string SourcePath,
    int DeclarationLine,
    int UsageLine,
    int UsageColumn,
    FindingConfidence Confidence = FindingConfidence.Medium,
    SourceSpan? DynamicSqlCallSite = null) : IRelocatableFinding<UnindexedTempTableUsageFinding>, IFinding
{
    public string RuleId { get; } = FindingRuleIds.UnindexedTempTableUsageRuleId(Kind);

    public SourceSpan Location => new(SourcePath, UsageLine, UsageColumn);
    int IRelocatableFinding.Line => UsageLine;
    int IRelocatableFinding.PositionColumn => UsageColumn;

    UnindexedTempTableUsageFinding IRelocatableFinding<UnindexedTempTableUsageFinding>.Relocated(SourceSpan span, SourceSpan? callSite, FindingConfidence confidence) =>
        this with { SourcePath = span.SourcePath, UsageLine = span.Line, UsageColumn = span.Column, DynamicSqlCallSite = callSite, Confidence = confidence };
}
