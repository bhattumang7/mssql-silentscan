using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", "silentscan/write-loss/temporal-precision-loss")]
public sealed class TemporalMidnightSourceOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(TemporalMidnightSourceOracleTests);

    protected override string Ddl => "SELECT 1;";

    private async Task<int> RoundTripKeepsTheValueAsync(string expression) =>
        await ScalarAsync<int>(
            $"DECLARE @x DATETIME = {expression}; DECLARE @d DATE = @x; SELECT CASE WHEN CAST(@d AS DATETIME) = @x THEN 1 ELSE 0 END;");

    [Theory]
    [InlineData("CONVERT(DATETIME, CONVERT(CHAR(8), 20240115))")]
    [InlineData("CAST(CAST(20240115 AS VARCHAR(8)) AS DATETIME)")]
    [InlineData("CAST(CAST('20240115 13:45' AS DATE) AS DATETIME)")]
    [InlineData("DATEADD(DAY, DATEDIFF(DAY, 0, '20240115 13:45'), 0)")]
    [InlineData("DATEADD(MONTH, DATEDIFF(MONTH, 0, '20240115 13:45'), 0)")]
    [InlineData("DATEADD(DAY, 1, DATEADD(DAY, DATEDIFF(DAY, 0, '20240115 13:45'), 0))")]
    [InlineData("DATEADD(DAY, DATEDIFF(DAY, '19000101', '20240115 13:45'), '19000101')")]
    [InlineData("DATEADD(DAY, 5, 1)")]
    public async Task ProvablyMidnightSource_LosesNothingWhenAssignedToDate(string expression)
    {
        Assert.Equal(1, await RoundTripKeepsTheValueAsync(expression));
    }

    [Theory]
    [InlineData("CAST('20240115 13:45' AS DATETIME)")]
    [InlineData("DATEADD(HOUR, DATEDIFF(HOUR, 0, '20240115 13:45'), 0)")]
    [InlineData("DATEADD(DAY, 1, CAST('20240115 13:45' AS DATETIME))")]
    public async Task SourceThatMayCarryATime_LosesItWhenAssignedToDate(string expression)
    {
        Assert.Equal(0, await RoundTripKeepsTheValueAsync(expression));
    }
}
