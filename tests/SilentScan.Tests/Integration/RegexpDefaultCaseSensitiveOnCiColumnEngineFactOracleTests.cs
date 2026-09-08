using Microsoft.Data.SqlClient;
using SilentScan.Verify;
using SilentScan.Verify.Deployment;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class RegexpDefaultCaseSensitiveOnCiColumnEngineFactOracleTests : IAsyncLifetime
{
    private static readonly SqlServerOptions Options = new(
        Host: "localhost",
        Port: int.TryParse(Environment.GetEnvironmentVariable("SILENTSCAN_SQL2025_PORT"), out var port) ? port : 14331,
        UserId: "sa",
        Password: Environment.GetEnvironmentVariable("SILENTSCAN_SA_PASSWORD") ?? "SilentScan!Dev2026");

    private readonly string _databaseName = $"{nameof(RegexpDefaultCaseSensitiveOnCiColumnEngineFactOracleTests)}_{Guid.NewGuid():N}";

    private const string Ddl = """
        ALTER DATABASE CURRENT SET COMPATIBILITY_LEVEL = 170;
        GO
        CREATE TABLE dbo.Customer (Name NVARCHAR(40) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL);
        INSERT INTO dbo.Customer (Name) VALUES ('John'), ('john'), ('JOHN'), ('Mary');
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
    public async Task LikePredicate_OnCiColumn_MatchesAllCasings()
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(_databaseName));
        await connection.OpenAsync();

        Assert.Equal(3, await CountAsync(connection, "Name = 'john'"));
        Assert.Equal(3, await CountAsync(connection, "Name LIKE 'john'"));
    }

    [Fact]
    public async Task RegexpLikeWithNoMatchType_OnCiColumn_MatchesOnlyLiteralCase()
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(_databaseName));
        await connection.OpenAsync();

        Assert.Equal(1, await CountAsync(connection, "REGEXP_LIKE(Name, 'john')"));
    }

    [Fact]
    public async Task RegexpLikeWithExplicitIFlag_OnCiColumn_MatchesAllCasings()
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(_databaseName));
        await connection.OpenAsync();

        Assert.Equal(3, await CountAsync(connection, "REGEXP_LIKE(Name, 'john', 'i')"));
    }

    [Fact]
    public async Task RegexpLikeWithCThenIFlag_LastFlagWins_MatchesAllCasings()
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(_databaseName));
        await connection.OpenAsync();

        Assert.Equal(3, await CountAsync(connection, "REGEXP_LIKE(Name, 'john', 'ci')"));
    }

    [Fact]
    public async Task RegexpLikeWithIThenCFlag_LastFlagWins_MatchesOnlyLiteralCase()
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(_databaseName));
        await connection.OpenAsync();

        Assert.Equal(1, await CountAsync(connection, "REGEXP_LIKE(Name, 'john', 'ic')"));
    }
}
