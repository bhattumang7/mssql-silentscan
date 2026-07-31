namespace SilentScan.Bench.Execution;

public sealed record QueryStatistics(long LogicalReads, long CpuMs, long ElapsedMs);
