using Microsoft.Data.SqlClient;
using SilentScan.Verify;
using SilentScan.Verify.Deployment;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class RegexpAccentInsensitiveColumnEngineFactOracleTests : IAsyncLifetime
{
    private static readonly SqlServerOptions Options = new(
        Host: "localhost",
        Port: int.TryParse(Environment.GetEnvironmentVariable("SILENTSCAN_SQL2025_PORT"), out var port) ? port : 14331,
        UserId: "sa",
        Password: Environment.GetEnvironmentVariable("SILENTSCAN_SA_PASSWORD") ?? "SilentScan!Dev2026");

    private readonly string _databaseName = $"{nameof(RegexpAccentInsensitiveColumnEngineFactOracleTests)}_{Guid.NewGuid():N}";

    private const string Ddl = """
        ALTER DATABASE CURRENT SET COMPATIBILITY_LEVEL = 170;
        GO
        CREATE TABLE dbo.Customer (Name NVARCHAR(40) COLLATE SQL_Latin1_General_CP1_CI_AI NOT NULL);
        INSERT INTO dbo.Customer (Name) VALUES (N'café');
        GO
        """;

    public async Task InitializeAsync()
    {
        await new DatabaseProvisioner(Options).CreateFreshAsync(_databaseName);
        await new ScriptDeployer(Options).DeployAsync(Ddl, _databaseName);
    }

    public async Task DisposeAsync() =>
        await new DatabaseProvisioner(Options).DropIfExistsAsync(_databaseName);

    private static async Task<int> CountAsync(SqlConnection connection, string predicate)
    {
        await using var command = new SqlCommand($"SELECT COUNT(*) FROM dbo.Customer WHERE {predicate};", connection);
        return (int)(await command.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task EqualityPredicate_OnAiColumn_FoldsAccents()
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(_databaseName));
        await connection.OpenAsync();

        Assert.Equal(1, await CountAsync(connection, "Name = 'cafe'"));
    }

    [Fact]
    public async Task RegexpLikeWithNoMatchType_OnAiColumn_NeverFoldsAccents()
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(_databaseName));
        await connection.OpenAsync();

        Assert.Equal(0, await CountAsync(connection, "REGEXP_LIKE(Name, 'cafe')"));
    }

    [Theory]
    [InlineData("c")]
    [InlineData("i")]
    [InlineData("s")]
    [InlineData("m")]
    [InlineData("im")]
    public async Task RegexpLikeWithAnyValidFlagCombination_NeverFoldsAccents(string matchType)
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(_databaseName));
        await connection.OpenAsync();

        Assert.Equal(0, await CountAsync(connection, $"REGEXP_LIKE(Name, 'cafe', '{matchType}')"));
    }

    [Fact]
    public async Task RegexpLikeWithAccentFlagLetter_RaisesInvalidFlagError()
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(_databaseName));
        await connection.OpenAsync();

        var exception = await Assert.ThrowsAsync<SqlException>(() => CountAsync(connection, "REGEXP_LIKE(Name, 'cafe', 'a')"));

        Assert.Equal(19303, exception.Number);
    }
}
