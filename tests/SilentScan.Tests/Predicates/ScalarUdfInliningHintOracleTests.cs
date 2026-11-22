using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.ScalarUdfPredicateInvocationRuleId)]
[Trait("Rule", FindingRuleIds.ScalarUdfProjectionInvocationRuleId)]
public sealed class ScalarUdfInliningHintOracleTests : OracleTestFixture
{
    private const int RowCount = 50;

    protected override string DatabaseNameSeed => nameof(ScalarUdfInliningHintOracleTests);

    protected override string Ddl => $"""
        CREATE TABLE dbo.Source (Id INT NOT NULL PRIMARY KEY);
        GO
        INSERT INTO dbo.Source (Id) SELECT TOP ({RowCount}) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) FROM sys.all_objects;
        GO
        CREATE FUNCTION dbo.Plain(@x INT) RETURNS INT AS BEGIN RETURN @x + 1; END;
        GO
        """;

    private async Task<long> UdfExecutionsAsync(string statement)
    {
        const string counter = "SELECT ISNULL(SUM(execution_count), 0) FROM sys.dm_exec_function_stats WHERE database_id = DB_ID() AND object_id = OBJECT_ID('dbo.Plain');";
        var before = await ScalarAsync<long>(counter);
        await ExecuteAsync(statement);
        return await ScalarAsync<long>(counter) - before;
    }

    [Theory]
    [InlineData("")]
    [InlineData("OPTION (USE HINT('QUERY_OPTIMIZER_COMPATIBILITY_LEVEL_150'))")]
    public async Task InlineableFunctionWithoutAnInliningBlock_IsNeverCalledPerRow(string option)
    {
        Assert.Equal(0, await UdfExecutionsAsync($"SELECT Id FROM dbo.Source WHERE dbo.Plain(Id) > 0 {option};"));
    }

    [Theory]
    [InlineData("OPTION (USE HINT('DISABLE_TSQL_SCALAR_UDF_INLINING'))")]
    [InlineData("OPTION (USE HINT('QUERY_OPTIMIZER_COMPATIBILITY_LEVEL_140'))")]
    public async Task InlineableFunctionWithAnInliningDisablingHint_IsCalledOncePerRow(string option)
    {
        Assert.Equal(RowCount, await UdfExecutionsAsync($"SELECT Id FROM dbo.Source WHERE dbo.Plain(Id) > 0 {option};"));
    }

    [Fact]
    public async Task InlineableFunctionWithInliningSwitchedOffForTheDatabase_IsCalledOncePerRow()
    {
        const string statement = "SELECT Id FROM dbo.Source WHERE dbo.Plain(Id) > 0;";
        Assert.Equal(0, await UdfExecutionsAsync(statement));

        await ExecuteAsync("ALTER DATABASE SCOPED CONFIGURATION SET TSQL_SCALAR_UDF_INLINING = OFF;");

        Assert.Equal(RowCount, await UdfExecutionsAsync(statement));
    }
}
