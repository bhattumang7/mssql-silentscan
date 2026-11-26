using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using SilentScan.Core.Rules;
using SilentScan.Verify.Deployment;

namespace SilentScan.Verify.Oracle;

public sealed partial class RescueMatrixGenerator
{
    private readonly DatabaseProvisioner _provisioner;
    private readonly SqlServerOptions _options;

    public RescueMatrixGenerator(SqlServerOptions options)
    {
        _options = options;
        _provisioner = new DatabaseProvisioner(options);
    }

    public async Task<(IReadOnlyList<RescueCell> Cells, string ServerVersion)> GenerateAsync(
        IReadOnlyList<RescueScenario> scenarios, CancellationToken cancellationToken = default)
    {
        var database = "SilentScanRescue_" + Guid.NewGuid().ToString("N")[..8];
        SqlConnection.ClearAllPools();
        await _provisioner.CreateFreshAsync(database, cancellationToken: cancellationToken);
        try
        {
            var tables = new Dictionary<string, string>();
            var cells = new List<RescueCell>();
            foreach (var scenario in scenarios)
            {
                if (!tables.TryGetValue(scenario.Table, out var table))
                {
                    table = "r" + tables.Count.ToString(CultureInfo.InvariantCulture);
                    tables[scenario.Table] = table;
                    await PrepareAsync(database, scenario.Table.Replace("{t}", table, StringComparison.Ordinal), table, cancellationToken);
                }

                cells.Add(new RescueCell(
                    scenario.Id, scenario.Family, scenario.Predicate,
                    await ProbeAsync(database, Query(table, scenario, "1 AS x"), cancellationToken),
                    await ProbeAsync(database, Query(table, scenario, "filler"), cancellationToken)));
            }

            return (cells, await ReadServerVersionAsync(database, cancellationToken));
        }
        finally
        {
            await _provisioner.DropIfExistsAsync(database, cancellationToken);
            SqlConnection.ClearAllPools();
        }
    }

    internal static string Query(string table, RescueScenario scenario, string projection) =>
        $"SELECT {projection} FROM {table} WHERE {scenario.Predicate}";

    internal static string Classify(string planXml)
    {
        var scanOrNone = ScanRegex().IsMatch(planXml) ? RescueMatrix.ScanOutcome : RescueMatrix.NoTableAccessOutcome;
        var access = SeekRegex().IsMatch(planXml) ? RescueMatrix.SeekOutcome : scanOrNone;
        return planXml.Contains("PlanAffectingConvert", StringComparison.Ordinal) ? access + "+Convert" : access;
    }

    private async Task PrepareAsync(string database, string ddl, string table, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_options.BuildConnectionString(database));
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(ddl + $" UPDATE STATISTICS {table} WITH ROWCOUNT = 1000000, PAGECOUNT = 100000;", connection) { CommandTimeout = 60 };
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<string> ProbeAsync(string database, string query, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_options.BuildConnectionString(database));
        await connection.OpenAsync(cancellationToken);
        try
        {
            await using (var on = new SqlCommand("SET SHOWPLAN_XML ON;", connection))
            {
                await on.ExecuteNonQueryAsync(cancellationToken);
            }

            await using var command = new SqlCommand(query, connection) { CommandTimeout = 60 };
            return Classify((string?)await command.ExecuteScalarAsync(cancellationToken) ?? string.Empty);
        }
        catch (SqlException ex)
        {
            return "Error:" + ex.Number.ToString(CultureInfo.InvariantCulture);
        }
    }

    private async Task<string> ReadServerVersionAsync(string database, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_options.BuildConnectionString(database));
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("SELECT CAST(SERVERPROPERTY('ProductVersion') AS VARCHAR(50));", connection);
        return (string)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    [GeneratedRegex("PhysicalOp=\"(Clustered )?Index Seek\"")]
    private static partial Regex SeekRegex();

    [GeneratedRegex("PhysicalOp=\"(Clustered )?(Index|Table) Scan\"")]
    private static partial Regex ScanRegex();
}
