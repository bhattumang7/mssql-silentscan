using Microsoft.Data.SqlClient;
using SilentScan.Bench.Reporting;
using SilentScan.Bench.Scenarios;
using SilentScan.Verify;
using SilentScan.Verify.Deployment;

namespace SilentScan.Bench.Execution;

/// <summary>
/// Runs the full CLAUDE.md Benchmark protocol matrix: per scenario x row count x CE mode x
/// matched/mismatched, median-of-5 warm runs. Compat level pinned to 160 (via
/// DatabaseProvisioner), MAXDOP 1 for reproducibility.
/// </summary>
public sealed class BenchmarkRunner(SqlServerOptions options)
{
    private const int WarmRuns = 1;
    private const int TimedRuns = 5;

    public async Task<IReadOnlyList<BenchmarkResult>> RunAsync(
        string databaseName,
        IReadOnlyList<TypePairScenario> scenarios,
        IReadOnlyList<int> rowCounts,
        CancellationToken cancellationToken = default)
    {
        await new DatabaseProvisioner(options).CreateFreshAsync(databaseName, cancellationToken);

        await using var connection = new SqlConnection(options.BuildConnectionString(databaseName));
        await connection.OpenAsync(cancellationToken);
        await SetMaxDopAsync(connection, cancellationToken);
        await EnableStatisticsCaptureAsync(connection, cancellationToken);

        var results = new List<BenchmarkResult>();
        foreach (var scenario in scenarios)
        {
            foreach (var rowCount in rowCounts)
            {
                var tableName = $"Bench_{scenario.Name}_{rowCount}";
                await SyntheticTableSeeder.SeedAsync(connection, scenario, tableName, rowCount, cancellationToken);

                foreach (var legacyCe in new[] { true, false })
                {
                    await SetLegacyCardinalityEstimationAsync(connection, legacyCe, cancellationToken);

                    results.Add(await RunCellAsync(connection, scenario, tableName, rowCount, legacyCe, matched: true, cancellationToken));
                    results.Add(await RunCellAsync(connection, scenario, tableName, rowCount, legacyCe, matched: false, cancellationToken));
                }
            }
        }

        return results;
    }

    private static async Task<BenchmarkResult> RunCellAsync(
        SqlConnection connection, TypePairScenario scenario, string tableName, int rowCount, bool legacyCe, bool matched, CancellationToken cancellationToken)
    {
        var probeRow = rowCount / 2;
        var paramTypeDdl = matched ? scenario.MatchedParamTypeDdl : scenario.MismatchedParamTypeDdl;
        var paramValue = matched ? scenario.MatchedParamValueForRow(probeRow) : scenario.MismatchedParamValueForRow(probeRow);
        var query = $"""
            DECLARE @p {paramTypeDdl} = {paramValue};
            SELECT Id FROM dbo.{tableName} WHERE Code = @p OPTION (MAXDOP 1);
            """;

        for (var i = 0; i < WarmRuns; i++)
        {
            await StatisticsCapture.CaptureAsync(connection, query, cancellationToken);
        }

        var runs = new List<QueryStatistics>();
        for (var i = 0; i < TimedRuns; i++)
        {
            runs.Add(await StatisticsCapture.CaptureAsync(connection, query, cancellationToken));
        }

        return new BenchmarkResult(
            scenario.Name,
            rowCount,
            legacyCe,
            matched,
            Median(runs.Select(r => r.LogicalReads)),
            Median(runs.Select(r => r.CpuMs)),
            Median(runs.Select(r => r.ElapsedMs)));
    }

    private static long Median(IEnumerable<long> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 0 ? (sorted[mid - 1] + sorted[mid]) / 2 : sorted[mid];
    }

    private static async Task SetMaxDopAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "ALTER DATABASE SCOPED CONFIGURATION SET MAXDOP = 1;";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnableStatisticsCaptureAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SET STATISTICS IO ON; SET STATISTICS TIME ON;";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task SetLegacyCardinalityEstimationAsync(SqlConnection connection, bool enabled, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"ALTER DATABASE SCOPED CONFIGURATION SET LEGACY_CARDINALITY_ESTIMATION = {(enabled ? "ON" : "OFF")};";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
