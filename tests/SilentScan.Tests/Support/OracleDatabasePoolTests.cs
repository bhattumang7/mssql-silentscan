using Microsoft.Data.SqlClient;
using SilentScan.Verify;

namespace SilentScan.Tests.Support;

public sealed class OracleDatabasePoolTests
{
    private const string Setup = """
        CREATE TABLE dbo.T (Id INT IDENTITY(1, 1) NOT NULL, A INT NOT NULL);
        GO
        INSERT INTO dbo.T (A) VALUES (1), (2), (3);
        GO
        """;

    private static readonly SqlServerOptions Options = SqlServerOptions.LocalDocker;

    [Fact]
    public async Task SameSetupAndLevel_ReusesTheDatabase()
    {
        await using var pool = new OracleDatabasePool();

        var first = await pool.UseAsync(Options, 160, Setup, Task.FromResult);
        var second = await pool.UseAsync(Options, 160, Setup, Task.FromResult);
        var otherLevel = await pool.UseAsync(Options, 110, Setup, Task.FromResult);

        Assert.Equal(first, second);
        Assert.NotEqual(first, otherLevel);
    }

    [Fact]
    public async Task StatisticsCreatedByOneUse_AreGoneForTheNext()
    {
        await using var pool = new OracleDatabasePool();
        const string CountStatistics = "SELECT COUNT(*) FROM sys.stats WHERE object_id = OBJECT_ID(N'dbo.T') AND auto_created = 1;";

        var first = await pool.UseAsync(Options, 160, Setup, async name =>
        {
            await ScalarAsync(name, "SELECT COUNT(*) FROM dbo.T WHERE A = 2;");
            Assert.Equal(1, await ScalarAsync(name, CountStatistics));
            return name;
        });
        var second = await pool.UseAsync(Options, 160, Setup, async name =>
        {
            Assert.Equal(0, await ScalarAsync(name, CountStatistics));
            return name;
        });

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task ConsumedIdentityValue_DiscardsTheDatabase()
    {
        await using var pool = new OracleDatabasePool();

        var first = await pool.UseAsync(Options, 160, Setup, async name =>
        {
            await ScalarAsync(name, "BEGIN TRANSACTION; INSERT INTO dbo.T (A) VALUES (4); ROLLBACK; SELECT 0;");
            return name;
        });
        var second = await pool.UseAsync(Options, 160, Setup, Task.FromResult);

        Assert.NotEqual(first, second);
        Assert.Equal(0, await ScalarAsync("master", $"SELECT COUNT(*) FROM sys.databases WHERE name = N'{first}';"));
    }

    [Fact]
    public async Task Disposal_DropsTheKeptDatabases()
    {
        string name;
        await using (var pool = new OracleDatabasePool())
        {
            name = await pool.UseAsync(Options, 160, Setup, Task.FromResult);
        }

        Assert.Equal(0, await ScalarAsync("master", $"SELECT COUNT(*) FROM sys.databases WHERE name = N'{name}';"));
    }

    private static async Task<int> ScalarAsync(string database, string sql)
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(database));
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        return (int)(await command.ExecuteScalarAsync())!;
    }
}
