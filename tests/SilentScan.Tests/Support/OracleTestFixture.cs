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

    protected static async Task<T?> ScalarAsync<T>(SqlConnection connection, string sql)
    {
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 120 };
        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? default : (T)Convert.ChangeType(value, Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T), System.Globalization.CultureInfo.InvariantCulture);
    }

    protected async Task<List<object?[]>> RowsAsync(string sql)
    {
        await using var connection = await OpenConnectionAsync();
        return await RowsAsync(connection, sql);
    }

    protected static async Task<List<object?[]>> RowsAsync(SqlConnection connection, string sql)
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
    }

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
}
