using System.Globalization;
using Microsoft.Data.SqlClient;
using SilentScan.Verify.Deployment;

namespace SilentScan.Verify.Oracle;

public sealed record ScopeObservation(string Id, string Observed);

public sealed class ScopeScenarioRunner
{
    public const string CaseSensitiveCollation = "Latin1_General_CS_AS";

    private const string PlaceholderPrefix = "{P}";

    private const int IncompleteDependenciesWarning = 2020;

    private const string NoDependencies = "(none)";

    private readonly SqlServerOptions _options;
    private readonly DatabaseProvisioner _provisioner;
    private readonly ScriptDeployer _deployer;

    public ScopeScenarioRunner(SqlServerOptions options)
    {
        _options = options;
        _provisioner = new DatabaseProvisioner(options);
        _deployer = new ScriptDeployer(options);
    }

    public async Task<(IReadOnlyList<ScopeObservation> Observations, string ServerVersion)> RunAsync(
        IReadOnlyList<ScopeScenario> scenarios, CancellationToken cancellationToken = default)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var caseInsensitiveDb = "SilentScanScopeCi_" + suffix;
        var caseSensitiveDb = "SilentScanScopeCs_" + suffix;
        SqlConnection.ClearAllPools();
        await _provisioner.CreateFreshAsync(caseInsensitiveDb, cancellationToken: cancellationToken);
        await _provisioner.CreateFreshAsync(caseSensitiveDb, CaseSensitiveCollation, cancellationToken);
        var observations = new List<ScopeObservation>();
        try
        {
            for (var i = 0; i < scenarios.Count; i++)
            {
                var scenario = scenarios[i];
                var database = scenario.CaseSensitiveDatabase ? caseSensitiveDb : caseInsensitiveDb;
                var prefix = "s" + i.ToString("00", CultureInfo.InvariantCulture) + "_";
                observations.Add(new ScopeObservation(scenario.Id, await ObserveAsync(scenario, database, prefix, cancellationToken)));
            }

            var version = await ReadServerVersionAsync(caseInsensitiveDb, cancellationToken);
            return (observations, version);
        }
        finally
        {
            await _provisioner.DropIfExistsAsync(caseInsensitiveDb, cancellationToken);
            await _provisioner.DropIfExistsAsync(caseSensitiveDb, cancellationToken);
            SqlConnection.ClearAllPools();
        }
    }

    private async Task<string> ObserveAsync(ScopeScenario scenario, string database, string prefix, CancellationToken cancellationToken)
    {
        if (scenario.Setup.Length > 0)
        {
            await _deployer.DeployAsync(scenario.Setup.Replace(PlaceholderPrefix, prefix, StringComparison.Ordinal), database, cancellationToken);
        }

        var body = scenario.Body.Replace(PlaceholderPrefix, prefix, StringComparison.Ordinal);
        await using var connection = new SqlConnection(_options.BuildConnectionString(database));
        await connection.OpenAsync(cancellationToken);
        try
        {
            return scenario.Kind == ScopeObservableKind.Witness
                ? await ReadWitnessAsync(connection, body, cancellationToken)
                : await ReadBindingAsync(connection, body, prefix, cancellationToken);
        }
        catch (SqlException ex)
        {
            return "Error:" + ex.Number.ToString(CultureInfo.InvariantCulture);
        }
    }

    private static async Task<string> ReadWitnessAsync(SqlConnection connection, string body, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("SET NOCOUNT ON; " + body, connection) { CommandTimeout = 60 };
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return "NoRows";
        }

        return reader.IsDBNull(0) ? "NULL" : Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture) ?? "NULL";
    }

    private static async Task<string> ReadBindingAsync(SqlConnection connection, string createModule, string prefix, CancellationToken cancellationToken)
    {
        await using (var create = new SqlCommand(createModule, connection) { CommandTimeout = 60 })
        {
            await create.ExecuteNonQueryAsync(cancellationToken);
        }

        var lines = new List<string>();
        var query = $"SELECT ISNULL(referenced_schema_name, '?'), referenced_entity_name, ISNULL(referenced_minor_name, '') "
            + $"FROM sys.dm_sql_referenced_entities(N'dbo.{prefix}m', N'OBJECT') ORDER BY 1, 2, 3;";
        await using var command = new SqlCommand(query, connection) { CommandTimeout = 60 };
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        try
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var column = reader.GetString(2);
                lines.Add(reader.GetString(0) + "." + reader.GetString(1).Replace(prefix, "P_", StringComparison.Ordinal) + (column.Length > 0 ? "." + column : string.Empty));
            }
        }
        catch (SqlException ex) when (ex.Number == IncompleteDependenciesWarning)
        {
            lines.Add("warning:" + IncompleteDependenciesWarning.ToString(CultureInfo.InvariantCulture));
        }

        return lines.Count == 0 ? NoDependencies : string.Join(";", lines);
    }

    private async Task<string> ReadServerVersionAsync(string database, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_options.BuildConnectionString(database));
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("SELECT CAST(SERVERPROPERTY('ProductVersion') AS VARCHAR(50));", connection);
        return (string)(await command.ExecuteScalarAsync(cancellationToken))!;
    }
}
