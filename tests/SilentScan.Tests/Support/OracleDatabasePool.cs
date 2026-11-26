using System.Collections.Concurrent;
using Microsoft.Data.SqlClient;
using SilentScan.Verify;
using SilentScan.Verify.Deployment;

namespace SilentScan.Tests.Support;

public sealed class OracleDatabasePool : IAsyncDisposable
{
    private const string UserStatistics = """
        FROM sys.stats AS s
        JOIN sys.objects AS o ON o.object_id = s.object_id AND o.is_ms_shipped = 0
        """;

    private const string StatisticNames = "SELECT CONCAT(s.object_id, N':', s.stats_id) " + UserStatistics + ";";

    private const string Fingerprint = """
        SELECT CONCAT(
            (SELECT STRING_AGG(CAST(CONCAT(object_id, N':', column_id, N':', CONVERT(nvarchar(100), last_value)) AS nvarchar(max)), N',')
                    WITHIN GROUP (ORDER BY object_id, column_id)
             FROM sys.identity_columns),
            N'|',
            (SELECT STRING_AGG(CAST(CONCAT(object_id, N':', CONVERT(nvarchar(100), current_value)) AS nvarchar(max)), N',')
                    WITHIN GROUP (ORDER BY object_id)
             FROM sys.sequences),
            N'|',
            (SELECT STRING_AGG(CAST(CONCAT(s.object_id, N':', s.stats_id, N':', CONVERT(nvarchar(33), p.last_updated, 126), N':', p.rows, N':', p.modification_counter) AS nvarchar(max)), N',')
                    WITHIN GROUP (ORDER BY s.object_id, s.stats_id)
             FROM sys.stats AS s
             JOIN sys.objects AS o ON o.object_id = s.object_id AND o.is_ms_shipped = 0
             OUTER APPLY sys.dm_db_stats_properties(s.object_id, s.stats_id) AS p));
        """;

    private const string DropNewStatistics = """
        DECLARE @drop nvarchar(max) = N'';
        SELECT @drop += N'DROP STATISTICS ' + QUOTENAME(SCHEMA_NAME(o.schema_id)) + N'.' + QUOTENAME(o.name) + N'.' + QUOTENAME(s.name) + N';'
        FROM sys.stats AS s
        JOIN sys.objects AS o ON o.object_id = s.object_id AND o.is_ms_shipped = 0
        WHERE s.auto_created = 1
          AND CHARINDEX(CONCAT(N',', s.object_id, N':', s.stats_id, N','), CONCAT(N',', @kept, N',')) = 0;
        EXEC sys.sp_executesql @drop;
        ALTER DATABASE SCOPED CONFIGURATION CLEAR PROCEDURE_CACHE;
        """;

    private readonly ConcurrentDictionary<(SqlServerOptions, int?, string), PooledDatabase> _idle = new();

    public async Task<T> UseAsync<T>(SqlServerOptions options, int? compatibilityLevel, string setup, Func<string, Task<T>> use)
    {
        var key = (options, compatibilityLevel, setup);
        if (!_idle.TryRemove(key, out var database))
        {
            database = await CreateAsync(options, compatibilityLevel, setup);
        }

        var kept = false;
        try
        {
            var result = await use(database.Name);
            kept = await ResetAsync(database) && _idle.TryAdd(key, database);
            return result;
        }
        finally
        {
            if (!kept)
            {
                await DropAsync(database);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var key in _idle.Keys.ToList())
        {
            if (_idle.TryRemove(key, out var database))
            {
                await DropAsync(database);
            }
        }
    }

    private static async Task<PooledDatabase> CreateAsync(SqlServerOptions options, int? compatibilityLevel, string setup)
    {
        var name = $"SilentScanRuleOracle_{Guid.NewGuid():N}";
        var provisioner = new DatabaseProvisioner(options);
        await provisioner.CreateFreshAsync(name);
        var created = false;
        try
        {
            if (compatibilityLevel is { } level)
            {
                await provisioner.SetCompatibilityLevelAsync(name, level);
            }

            if (!string.IsNullOrWhiteSpace(setup))
            {
                await new ScriptDeployer(options).DeployAsync(setup, name);
            }

            await using var connection = await OpenAsync(options, name);
            var statistics = new List<string>();
            await using (var command = new SqlCommand(StatisticNames, connection))
            await using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    statistics.Add(reader.GetString(0));
                }
            }

            var database = new PooledDatabase(options, name, string.Join(',', statistics), await ReadFingerprintAsync(connection));
            created = true;
            return database;
        }
        finally
        {
            if (!created)
            {
                await provisioner.DropIfExistsAsync(name);
            }
        }
    }

    private static async Task<bool> ResetAsync(PooledDatabase database)
    {
        await using var connection = await OpenAsync(database.Options, database.Name);
        await using (var command = new SqlCommand(DropNewStatistics, connection))
        {
            command.Parameters.AddWithValue("@kept", database.Statistics);
            await command.ExecuteNonQueryAsync();
        }

        return string.Equals(await ReadFingerprintAsync(connection), database.Fingerprint, StringComparison.Ordinal);
    }

    private static async Task<SqlConnection> OpenAsync(SqlServerOptions options, string database)
    {
        var connection = new SqlConnection(options.BuildConnectionString(database));
        await connection.OpenAsync();
        return connection;
    }

    private static async Task<string> ReadFingerprintAsync(SqlConnection connection)
    {
        await using var command = new SqlCommand(Fingerprint, connection);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static Task DropAsync(PooledDatabase database) => new DatabaseProvisioner(database.Options).DropIfExistsAsync(database.Name);

    private sealed record PooledDatabase(SqlServerOptions Options, string Name, string Statistics, string Fingerprint);
}
