using Microsoft.Data.SqlClient;
using SilentScan.Verify;
using SilentScan.Verify.Deployment;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.RegexpDefaultCaseSensitiveOnCiColumnRuleId)]
public sealed class RegexpDefaultCaseSensitiveOnCiColumnEngineFactOracleTests : IAsyncLifetime
{
    private static readonly SqlServerOptions Options = SqlServerOptions.LocalDockerLatest;

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

    [Theory]
    [InlineData("'\\w+'", "", 4)]
    [InlineData("'[[:alpha:]]+'", "", 4)]
    [InlineData("'[[:upper:]]+'", "", 3)]
    [InlineData("'[[:upper:]]+'", ", 'i'", 4)]
    [InlineData("'\\p{Lu}+'", "", 3)]
    [InlineData("'\\p{Lu}+'", ", 'i'", 4)]
    [InlineData("'(?i)john'", "", 3)]
    [InlineData("'[Jj][Oo][Hh][Nn]'", "", 3)]
    [InlineData("'\\p{L}+'", "", 4)]
    [InlineData("'\\p{L}+'", ", 'i'", 4)]
    public async Task CaseNeutralEscapesMatchEveryCasing_WhileCaseCarryingClassesNeedTheIFlag(string pattern, string flags, int expected)
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(_databaseName));
        await connection.OpenAsync();

        Assert.Equal(expected, await CountAsync(connection, $"REGEXP_LIKE(Name, {pattern}{flags})"));
    }
}
