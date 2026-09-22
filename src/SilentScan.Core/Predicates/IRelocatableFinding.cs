using SilentScan.Core.Diagnostics;

namespace SilentScan.Core.Predicates;

internal interface IRelocatableFinding
{
    string SourcePath { get; }
    int Line { get; }
    int PositionColumn { get; }
    SourceSpan? DynamicSqlCallSite { get; }

    IFinding RelocatedAny(SourceSpan span, SourceSpan? callSite, FindingConfidence confidence);
}

internal interface IRelocatableFinding<TSelf> : IRelocatableFinding
    where TSelf : IFinding, IRelocatableFinding<TSelf>
{
    TSelf Relocated(SourceSpan span, SourceSpan? callSite, FindingConfidence confidence);

    IFinding IRelocatableFinding.RelocatedAny(SourceSpan span, SourceSpan? callSite, FindingConfidence confidence) =>
        Relocated(span, callSite, confidence);
}
