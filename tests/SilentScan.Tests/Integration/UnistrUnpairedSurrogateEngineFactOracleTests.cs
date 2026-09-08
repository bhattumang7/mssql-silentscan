using Microsoft.Data.SqlClient;
using SilentScan.Verify;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class UnistrUnpairedSurrogateEngineFactOracleTests
{
    private static readonly SqlServerOptions Options = new(
        Host: "localhost",
        Port: int.TryParse(Environment.GetEnvironmentVariable("SILENTSCAN_SQL2025_PORT"), out var port) ? port : 14331,
        UserId: "sa",
        Password: Environment.GetEnvironmentVariable("SILENTSCAN_SA_PASSWORD") ?? "SilentScan!Dev2026");

    [Fact]
    public async Task UnistrWithUnpairedHighSurrogate_IsAcceptedByLiveEngineWithNoError()
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString());
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT DATALENGTH(UNISTR(N'\\D800'));";

        var length = (int)(await command.ExecuteScalarAsync())!;
        Assert.Equal(2, length);
    }

    [Fact]
    public async Task JsonValue_OnJsonTextContainingTheSameUnpairedSurrogateEscape_AlsoResolvesWithNoError()
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString());
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ISJSON(N'{"a":"\uD800"}');
            """;

        var isValid = (int)(await command.ExecuteScalarAsync())!;
        Assert.Equal(1, isValid);
    }
}
