using Microsoft.Data.SqlClient;
using SilentScan.Verify;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
[Trait("Rule", "silentscan/string-builtin/unistr-unpaired-surrogate")]
public sealed class UnistrUnpairedSurrogateEngineFactOracleTests
{
    private static readonly SqlServerOptions Options = SqlServerOptions.LocalDockerLatest;

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
