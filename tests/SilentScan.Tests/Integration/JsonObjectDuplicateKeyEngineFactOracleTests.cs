using Microsoft.Data.SqlClient;
using SilentScan.Verify;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class JsonObjectDuplicateKeyEngineFactOracleTests
{
    private static readonly SqlServerOptions Options = SqlServerOptions.LocalDocker;

    [Fact]
    public async Task JsonObjectWithDuplicateKey_IsAcceptedByLiveEngineWithNoError()
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString());
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT JSON_OBJECT('a':1,'a':2);";

        var json = (string)(await command.ExecuteScalarAsync())!;
        Assert.Equal("""{"a":1,"a":2}""", json);
    }

    [Fact]
    public async Task JsonValue_OnObjectWithDuplicateKey_ResolvesToTheFirstOccurrence()
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString());
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT JSON_VALUE(JSON_OBJECT('a':1,'a':2), '$.a');";

        var value = (string)(await command.ExecuteScalarAsync())!;
        Assert.Equal("1", value);
    }
}
