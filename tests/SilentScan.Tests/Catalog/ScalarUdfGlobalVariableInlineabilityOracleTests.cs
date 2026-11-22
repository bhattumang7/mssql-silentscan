using SilentScan.Tests.Support;

namespace SilentScan.Tests.Catalog;

[Trait("Category", "Oracle")]
public sealed class ScalarUdfGlobalVariableInlineabilityOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(ScalarUdfGlobalVariableInlineabilityOracleTests);

    protected override string Ddl => """
        CREATE TABLE dbo.Anchor (Id INT NOT NULL);
        GO
        """;

    [Theory]
    [InlineData("@@ROWCOUNT", 0)]
    [InlineData("@@ERROR", 0)]
    [InlineData("@@NESTLEVEL", 0)]
    [InlineData("@@PROCID", 0)]
    [InlineData("@@CONNECTIONS", 0)]
    [InlineData("@@CPU_BUSY", 0)]
    [InlineData("@@IDLE", 0)]
    [InlineData("@@IO_BUSY", 0)]
    [InlineData("@@MAX_CONNECTIONS", 0)]
    [InlineData("@@PACKET_ERRORS", 0)]
    [InlineData("@@PACK_RECEIVED", 0)]
    [InlineData("@@PACK_SENT", 0)]
    [InlineData("@@TIMETICKS", 0)]
    [InlineData("@@TOTAL_ERRORS", 0)]
    [InlineData("@@TOTAL_READ", 0)]
    [InlineData("@@TOTAL_WRITE", 0)]
    [InlineData("@@SPID", 1)]
    [InlineData("@@TRANCOUNT", 1)]
    [InlineData("@@IDENTITY", 1)]
    [InlineData("@@LANGID", 1)]
    public async Task GlobalVariableInsideScalarUdf_TheEngineInlinesItExactlyWhenTheScannerSaysItDoesNot(string globalVariable, int expectedIsInlineable)
    {
        await ExecuteAsync($"CREATE FUNCTION dbo.fn_Probe (@x INT) RETURNS BIGINT AS BEGIN RETURN CAST({globalVariable} AS BIGINT) + @x; END");

        var isInlineable = await ScalarAsync<int>("SELECT CAST(is_inlineable AS INT) FROM sys.sql_modules WHERE object_id = OBJECT_ID('dbo.fn_Probe');");

        Assert.Equal(expectedIsInlineable, isInlineable);
    }
}
