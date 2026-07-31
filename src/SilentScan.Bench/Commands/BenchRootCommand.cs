using System.CommandLine;

namespace SilentScan.Bench.Commands;

/// <summary>
/// Root command for the benchmark harness (CLAUDE.md SilentScan.Bench): builds synthetic
/// tables per reported type pair at 10K/1M/10M rows and measures matched vs mismatched
/// predicate cost under both collation families and both cardinality estimators.
/// </summary>
public static class BenchRootCommand
{
    public static RootCommand Create() =>
        new("silentscan-bench — measures the logical-read/CPU cost of index-killing implicit conversions at scale.")
        {
            RunCommand.Create(),
        };
}
