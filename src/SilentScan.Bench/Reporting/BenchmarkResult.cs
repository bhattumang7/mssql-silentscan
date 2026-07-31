namespace SilentScan.Bench.Reporting;

/// <summary>One matched-vs-mismatched cell of the cost table (CLAUDE.md Benchmark protocol). Each metric is the median of 5 warm runs.</summary>
public sealed record BenchmarkResult(
    string ScenarioName,
    int RowCount,
    bool LegacyCardinalityEstimation,
    bool Matched,
    long MedianLogicalReads,
    long MedianCpuMs,
    long MedianElapsedMs);
