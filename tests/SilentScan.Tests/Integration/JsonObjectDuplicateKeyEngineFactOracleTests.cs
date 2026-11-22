using Microsoft.Data.SqlClient;
using SilentScan.Verify;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.JsonObjectDuplicateKeyRuleId)]
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

    [Theory]
    [InlineData("SELECT JSON_OBJECT('a':NULL,'a':2 ABSENT ON NULL);", """{"a":2}""")]
    [InlineData("SELECT JSON_OBJECT('a':1,'a':NULL ABSENT ON NULL);", """{"a":1}""")]
    [InlineData("SELECT JSON_OBJECT('a':NULL,'a':NULL ABSENT ON NULL);", "{}")]
    [InlineData("SELECT JSON_OBJECT('a':1,'a':NULL NULL ON NULL);", """{"a":1,"a":null}""")]
    [InlineData("SELECT JSON_OBJECT('a':1,'a':2 ABSENT ON NULL);", """{"a":1,"a":2}""")]
    public async Task AbsentOnNull_DropsALiteralNullEntry_SoOnlyTheSurvivingRepeatsStayDuplicated(string sql, string expected)
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString());
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        Assert.Equal(expected, (string)(await command.ExecuteScalarAsync())!);
    }
}
