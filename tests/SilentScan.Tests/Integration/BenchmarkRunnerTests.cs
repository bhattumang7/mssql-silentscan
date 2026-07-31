using SilentScan.Bench.Execution;
using SilentScan.Bench.Reporting;
using SilentScan.Bench.Scenarios;
using SilentScan.Verify;
using SilentScan.Verify.Deployment;

namespace SilentScan.Tests.Integration;

/// <summary>
/// Phase 5 exit criterion (plan.md): "the cost table that the writeup charts." Runs the real
/// benchmark harness against the live Docker SQL Server oracle at a small row count (full
/// 10K/1M/10M runs are a CLI operation, not part of the automated suite - inserting and
/// benchmarking 10M rows several times over would make `dotnet test` impractically slow).
/// The harness itself is unchanged between scales; what's verified here is that it produces
/// a real, meaningful signal: the mismatched (implicit-conversion) query costs measurably
/// more than the matched one.
/// </summary>
public sealed class BenchmarkRunnerTests : IAsyncLifetime
{
    private const string DatabaseName = "SilentScanBenchTest";
    private const int RowCount = 2_000;

    private readonly SqlServerOptions _options = SqlServerOptions.LocalDocker;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() =>
        await new DatabaseProvisioner(_options).DropIfExistsAsync(DatabaseName);

    [Fact]
    public async Task RunAsync_VarCharVsNVarChar_MismatchedCostsMoreThanMatched()
    {
        var scenario = TypePairScenario.VarCharVsNVarChar("SQL_Latin1_General_CP1_CI_AS");
        var runner = new BenchmarkRunner(_options);

        var results = await runner.RunAsync(DatabaseName, [scenario], [RowCount]);

        // 4 cells: matched/mismatched x legacy CE on/off.
        Assert.Equal(4, results.Count);

        foreach (var legacyCe in new[] { true, false })
        {
            var matched = results.Single(r => r.LegacyCardinalityEstimation == legacyCe && r.Matched);
            var mismatched = results.Single(r => r.LegacyCardinalityEstimation == legacyCe && !r.Matched);

            // SQL_* collation forces a scan on mismatch; a scan touches every row's worth of
            // pages, so its logical reads must be at least as high as a seek's - and for a
            // scenario shaped this way, meaningfully higher.
            Assert.True(
                mismatched.MedianLogicalReads > matched.MedianLogicalReads,
                $"Expected mismatched logical reads ({mismatched.MedianLogicalReads}) to exceed matched ({matched.MedianLogicalReads}) under legacyCe={legacyCe}.");
        }
    }

    [Fact]
    public async Task RunAsync_ResultsWriteToValidCsv()
    {
        var scenario = TypePairScenario.IntVsBigInt();
        var runner = new BenchmarkRunner(_options);

        var results = await runner.RunAsync(DatabaseName, [scenario], [RowCount]);
        var csv = CsvReportWriter.Write(results);
        var lines = csv.TrimEnd().Split('\n');

        Assert.Equal("ScenarioName,RowCount,LegacyCardinalityEstimation,Matched,MedianLogicalReads,MedianCpuMs,MedianElapsedMs", lines[0].TrimEnd('\r'));
        Assert.Equal(results.Count + 1, lines.Length);
    }
}
