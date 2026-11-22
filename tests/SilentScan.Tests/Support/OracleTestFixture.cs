using System.Xml.Linq;
using Microsoft.Data.SqlClient;
using SilentScan.Verify;
using SilentScan.Verify.Deployment;

namespace SilentScan.Tests.Support;

public abstract class OracleTestFixture : IAsyncLifetime
{
    protected SqlServerOptions Options { get; } = SqlServerOptions.LocalDocker;

    protected abstract string DatabaseNameSeed { get; }

    protected string DatabaseName => _databaseName ??= $"{DatabaseNameSeed}_{Guid.NewGuid():N}";

    private string? _databaseName;

    protected abstract string Ddl { get; }

    public virtual async Task InitializeAsync()
    {
        await new DatabaseProvisioner(Options).CreateFreshAsync(DatabaseName);
        await new ScriptDeployer(Options).DeployAsync(Ddl, DatabaseName);
    }

    public virtual async Task DisposeAsync()
    {
        SqlConnection.ClearAllPools();
        await new DatabaseProvisioner(Options).DropIfExistsAsync(DatabaseName);
    }

    protected async Task<int> CountBufferedPagesAsync()
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString());
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "SELECT COUNT(*) FROM sys.dm_os_buffer_descriptors WHERE database_id = DB_ID(@name);", connection);
        command.Parameters.AddWithValue("@name", DatabaseName);
        return (int)(await command.ExecuteScalarAsync())!;
    }

    protected async Task<SqlConnection> OpenConnectionAsync()
    {
        var connection = new SqlConnection(Options.BuildConnectionString(DatabaseName));
        await connection.OpenAsync();
        return connection;
    }

    protected async Task ExecuteAsync(string sql)
    {
        await using var connection = await OpenConnectionAsync();
        await ExecuteAsync(connection, sql);
    }

    protected static async Task ExecuteAsync(SqlConnection connection, string sql)
    {
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 120 };
        await command.ExecuteNonQueryAsync();
    }

    protected async Task<T?> ScalarAsync<T>(string sql)
    {
        await using var connection = await OpenConnectionAsync();
        return await ScalarAsync<T>(connection, sql);
    }

    private const int DatabaseInTransitionErrorNumber = 952;

    private const int ReadAttempts = 5;

    private static async Task<TResult> RetryWhileAnotherDatabaseIsInTransitionAsync<TResult>(Func<Task<TResult>> read)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await read();
            }
            catch (SqlException ex) when (ex.Number == DatabaseInTransitionErrorNumber && attempt < ReadAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250));
            }
        }
    }

    protected static Task<T?> ScalarAsync<T>(SqlConnection connection, string sql) =>
        RetryWhileAnotherDatabaseIsInTransitionAsync(async () =>
        {
            await using var command = new SqlCommand(sql, connection) { CommandTimeout = 120 };
            var value = await command.ExecuteScalarAsync();
            return value is null or DBNull ? default : (T)Convert.ChangeType(value, Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T), System.Globalization.CultureInfo.InvariantCulture);
        });

    protected async Task<List<object?[]>> RowsAsync(string sql)
    {
        await using var connection = await OpenConnectionAsync();
        return await RowsAsync(connection, sql);
    }

    protected static Task<List<object?[]>> RowsAsync(SqlConnection connection, string sql) =>
        RetryWhileAnotherDatabaseIsInTransitionAsync(async () =>
        {
            var rows = new List<object?[]>();
            await using var command = new SqlCommand(sql, connection) { CommandTimeout = 120 };
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var row = new object?[reader.FieldCount];
                for (var i = 0; i < row.Length; i++)
                {
                    row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                }

                rows.Add(row);
            }

            return rows;
        });

    protected async Task<int?> SqlErrorNumberAsync(string sql)
    {
        await using var connection = await OpenConnectionAsync();
        return await SqlErrorNumberAsync(connection, sql);
    }

    protected static async Task<int?> SqlErrorNumberAsync(SqlConnection connection, string sql)
    {
        try
        {
            await ExecuteAsync(connection, sql);
            return null;
        }
        catch (SqlException ex)
        {
            return ex.Number;
        }
    }

    protected async Task<XDocument> PlanInSessionAsync(string setupSql, string probeSql)
    {
        await using var connection = await OpenConnectionAsync();
        if (!string.IsNullOrWhiteSpace(setupSql))
        {
            await ExecuteAsync(connection, setupSql);
        }

        await ExecuteAsync(connection, "SET SHOWPLAN_XML ON;");
        string? planXml = null;
        try
        {
            await using var command = new SqlCommand(probeSql, connection) { CommandTimeout = 120 };
            await using var reader = await command.ExecuteReaderAsync();
            do
            {
                while (await reader.ReadAsync())
                {
                    if (reader.FieldCount == 1 && reader.GetFieldType(0) == typeof(string))
                    {
                        var value = reader.GetString(0);
                        if (value.Contains("ShowPlanXML", StringComparison.Ordinal))
                        {
                            planXml = value;
                        }
                    }
                }
            }
            while (await reader.NextResultAsync());
        }
        finally
        {
            await ExecuteAsync(connection, "SET SHOWPLAN_XML OFF;");
        }

        Assert.False(string.IsNullOrEmpty(planXml), probeSql);
        return XDocument.Parse(planXml!);
    }

    protected async Task<string> CaptureActualPlanAsync(string probe)
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(DatabaseName));
        await connection.OpenAsync();

        await using (var onCommand = new SqlCommand("SET STATISTICS XML ON;", connection))
        {
            await onCommand.ExecuteNonQueryAsync();
        }

        string planXml;
        await using (var probeCommand = new SqlCommand(probe, connection))
        await using (var reader = await probeCommand.ExecuteReaderAsync())
        {
            planXml = string.Empty;
            do
            {
                while (await reader.ReadAsync())
                {
                    if (reader.FieldCount == 1 && reader.GetFieldType(0) == typeof(string))
                    {
                        var value = reader.GetString(0);
                        if (value.Contains("ShowPlanXML", StringComparison.Ordinal))
                        {
                            planXml = value;
                        }
                    }
                }
            }
            while (await reader.NextResultAsync());
        }

        await using (var offCommand = new SqlCommand("SET STATISTICS XML OFF;", connection))
        {
            await offCommand.ExecuteNonQueryAsync();
        }

        Assert.NotEmpty(planXml);
        return planXml;
    }
}
