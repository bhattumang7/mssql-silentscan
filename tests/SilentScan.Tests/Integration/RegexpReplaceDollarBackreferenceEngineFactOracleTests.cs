using Microsoft.Data.SqlClient;
using SilentScan.Verify;
using SilentScan.Verify.Deployment;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class RegexpReplaceDollarBackreferenceEngineFactOracleTests : IAsyncLifetime
{
    private static readonly SqlServerOptions Options = new(
        Host: "localhost",
        Port: int.TryParse(Environment.GetEnvironmentVariable("SILENTSCAN_SQL2025_PORT"), out var port) ? port : 14331,
        UserId: "sa",
        Password: Environment.GetEnvironmentVariable("SILENTSCAN_SA_PASSWORD") ?? "SilentScan!Dev2026");

    private readonly string _databaseName = $"{nameof(RegexpReplaceDollarBackreferenceEngineFactOracleTests)}_{Guid.NewGuid():N}";

    private const string Ddl = """
        ALTER DATABASE CURRENT SET COMPATIBILITY_LEVEL = 170;
        GO
        """;

    public async Task InitializeAsync()
    {
        await new DatabaseProvisioner(Options).CreateFreshAsync(_databaseName);
        await new ScriptDeployer(Options).DeployAsync(Ddl, _databaseName);
    }

    public async Task DisposeAsync() =>
        await new DatabaseProvisioner(Options).DropIfExistsAsync(_databaseName);

    private static async Task<string> ReplaceAsync(SqlConnection connection, string replacement)
    {
        await using var command = new SqlCommand(
            $"SELECT REGEXP_REPLACE('abc123def', '([a-z]+)([0-9]+)', '{replacement}');", connection);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task DollarDigitTokens_PassThroughLiterallyUnchanged()
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(_databaseName));
        await connection.OpenAsync();

        Assert.Equal("$2-$1def", await ReplaceAsync(connection, "$2-$1"));
    }

    [Fact]
    public async Task BackslashDigitTokens_SubstituteCapturedGroups()
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(_databaseName));
        await connection.OpenAsync();

        Assert.Equal("123-abcdef", await ReplaceAsync(connection, "\\2-\\1"));
    }
}
