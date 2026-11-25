namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
[Trait("Rule", SilentScan.Core.Rules.FindingRuleIds.UnderLengthParameterRuleId)]
public sealed class ParameterLengthDefaultOracleTests
{
    [Fact]
    public async Task UnsizedDeclaredVariableKeepsOneCharacter_WhileUnsizedCastKeepsThirty()
    {
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(SilentScan.Verify.SqlServerOptions.LocalDocker.BuildConnectionString("tempdb"));
        await connection.OpenAsync();
        await using var command = new Microsoft.Data.SqlClient.SqlCommand(
            "DECLARE @p VARCHAR = 'abcdef'; SELECT CAST(LEN(@p) AS VARCHAR(5)) + '|' + CAST(LEN(CAST('abcdefghijklmnopqrstuvwxyz0123456789' AS VARCHAR)) AS VARCHAR(5));", connection);

        Assert.Equal("1|30", (string)(await command.ExecuteScalarAsync())!);
    }
}
