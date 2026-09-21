using SilentScan.Tests.Support;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class WriteLossAndCallSiteEngineFactOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(WriteLossAndCallSiteEngineFactOracleTests);

    protected override string Ddl => """
        CREATE FUNCTION dbo.ItvfEcho (@p VARCHAR(5)) RETURNS TABLE AS RETURN SELECT @p AS V;
        GO
        CREATE TYPE dbo.AmountTable AS TABLE (Amt DECIMAL(10,2));
        GO
        CREATE PROCEDURE dbo.ReturnsFourDecimals @v DECIMAL(10,4) AS SELECT @v AS Amt;
        GO
        CREATE PROCEDURE dbo.ReadAmounts @t dbo.AmountTable READONLY AS SELECT Amt FROM @t;
        GO
        """;

    [Fact]
    [Trait("Rule", "silentscan/write-loss/temporal-offset-dropped")]
    public async Task DatetimeoffsetToDatetime2_KeepsLocalDigitsWithoutUtcAdjustment()
    {
        var kept = await ScalarAsync<string>("DECLARE @o DATETIMEOFFSET = '2020-01-01 10:00:00 +05:00'; DECLARE @d DATETIME2 = @o; SELECT CONVERT(VARCHAR(30), @d, 121);");
        var utc = await ScalarAsync<string>("DECLARE @o DATETIMEOFFSET = '2020-01-01 10:00:00 +05:00'; SELECT CONVERT(VARCHAR(30), CAST(SWITCHOFFSET(@o, '+00:00') AS DATETIME2), 121);");
        var noOffset = await ScalarAsync<string>("DECLARE @o DATETIMEOFFSET = '2020-01-01 10:00:00 +00:00'; DECLARE @d DATETIME2 = @o; SELECT CONVERT(VARCHAR(30), @d, 121);");

        Assert.Equal("2020-01-01 10:00:00.0000000", kept);
        Assert.Equal("2020-01-01 05:00:00.0000000", utc);
        Assert.Equal(kept, noOffset);
    }

    [Fact]
    [Trait("Rule", "silentscan/write-loss/temporal-precision-loss")]
    public async Task Datetime2ToDate_DropsTimeSilently_DatetimeTargetKeepsIt()
    {
        var date = await ScalarAsync<string>("DECLARE @s DATETIME2 = '2020-01-01 23:59:59'; DECLARE @d DATE = @s; SELECT CONVERT(VARCHAR(30), @d, 121);");
        var control = await ScalarAsync<string>("DECLARE @s DATETIME2 = '2020-01-01 23:59:59'; DECLARE @d DATETIME2(0) = @s; SELECT CONVERT(VARCHAR(30), @d, 121);");

        Assert.Equal("2020-01-01", date);
        Assert.Equal("2020-01-01 23:59:59", control);
    }

    [Fact]
    [Trait("Rule", "silentscan/write-loss/temporal-scale-narrowing")]
    public async Task Time7ToTime0_RoundsFractionalSeconds_SameScaleControlKeepsThem()
    {
        var narrowed = await ScalarAsync<string>("DECLARE @s TIME(7) = '10:00:00.9999999'; DECLARE @t TIME(0) = @s; SELECT CONVERT(VARCHAR(30), @t);");
        var control = await ScalarAsync<string>("DECLARE @s TIME(7) = '10:00:00.9999999'; DECLARE @t TIME(7) = @s; SELECT CONVERT(VARCHAR(30), @t);");

        Assert.Equal("10:00:01", narrowed);
        Assert.Equal("10:00:00.9999999", control);
    }

    [Fact]
    [Trait("Rule", "silentscan/call-graph/tvf-argument-type-mismatch")]
    public async Task InlineTvfArgumentLongerThanParameter_IsTruncated_FittingControlIsNot()
    {
        Assert.Equal("abcde", await ScalarAsync<string>("SELECT V FROM dbo.ItvfEcho('abcdefgh');"));
        Assert.Equal("abc", await ScalarAsync<string>("SELECT V FROM dbo.ItvfEcho('abc');"));
    }

    [Fact]
    [Trait("Rule", "silentscan/call-graph/table-valued-argument-column-mismatch")]
    public async Task TableVariableOfTableType_RoundsWideDecimalOnInsert_ExactControlIsUnchanged()
    {
        var rows = await RowsAsync("DECLARE @t dbo.AmountTable; INSERT INTO @t VALUES (1.239), (1.23); EXEC dbo.ReadAmounts @t;");

        Assert.Equal(1.24m, rows[0][0]);
        Assert.Equal(1.23m, rows[1][0]);
    }

    [Fact]
    [Trait("Rule", "silentscan/dynamic-sql/insert-exec-temp-table-column-type-mismatch")]
    public async Task InsertExecIntoNarrowerTempColumn_RoundsSilently_MatchingColumnControlKeepsPrecision()
    {
        await using var connection = await OpenConnectionAsync();
        await ExecuteAsync(connection, "CREATE TABLE #narrow (Amt DECIMAL(10,2)); CREATE TABLE #same (Amt DECIMAL(10,4));");
        await ExecuteAsync(connection, "INSERT INTO #narrow EXEC dbo.ReturnsFourDecimals @v = 1.2399; INSERT INTO #same EXEC dbo.ReturnsFourDecimals @v = 1.2399;");

        Assert.Equal(1.24m, await ScalarAsync<decimal>(connection, "SELECT Amt FROM #narrow;"));
        Assert.Equal(1.2399m, await ScalarAsync<decimal>(connection, "SELECT Amt FROM #same;"));
    }
}
