using Microsoft.Data.SqlClient;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class RowLimitOutOfRangeOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(RowLimitOutOfRangeOracleTests);

    private const string ScannerDdl = "CREATE TABLE dbo.T (Id INT NOT NULL PRIMARY KEY, V INT NOT NULL);";

    protected override string Ddl => $"""
        {ScannerDdl}
        INSERT INTO dbo.T VALUES (1, 1), (2, 2), (3, 3);
        """;

    private static IReadOnlyList<RowLimitOutOfRangeFinding> Scan(string sql)
    {
        var result = SqlScriptParser.ParseText("test.sql", $"{ScannerDdl}\n{sql}");
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        return RowLimitOutOfRangeScanner.Scan(result);
    }

    private async Task<SqlException> ExecuteExpectingFailureAsync(string sql)
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(DatabaseName));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await Assert.ThrowsAsync<SqlException>(() => command.ExecuteNonQueryAsync());
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(DatabaseName));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task NegativeTopLiteral_FailsWithMsg127_AndScannerFlagsIt()
    {
        var exception = await ExecuteExpectingFailureAsync("SELECT TOP (-1) * FROM dbo.T;");

        Assert.Equal(127, exception.Number);

        var finding = Assert.Single(Scan("SELECT TOP (-1) * FROM dbo.T;"));
        Assert.Equal(RowLimitOutOfRangeKind.TopRowCountNegative, finding.Kind);
    }

    [Fact]
    public async Task ZeroTopLiteral_Succeeds_AndScannerDoesNotFlagIt()
    {
        var exception = await Record.ExceptionAsync(() => ExecuteAsync("SELECT TOP (0) * FROM dbo.T;"));

        Assert.Null(exception);
        Assert.Empty(Scan("SELECT TOP (0) * FROM dbo.T;"));
    }

    [Fact]
    public async Task NegativeOffsetLiteral_FailsWithMsg10742_AndScannerFlagsIt()
    {
        var sql = "SELECT * FROM dbo.T ORDER BY Id OFFSET -1 ROWS;";
        var exception = await ExecuteExpectingFailureAsync(sql);

        Assert.Equal(10742, exception.Number);

        var finding = Assert.Single(Scan(sql));
        Assert.Equal(RowLimitOutOfRangeKind.OffsetNegative, finding.Kind);
    }

    [Fact]
    public async Task ZeroFetchLiteral_FailsWithMsg10744_AndScannerFlagsIt()
    {
        var sql = "SELECT * FROM dbo.T ORDER BY Id OFFSET 0 ROWS FETCH NEXT 0 ROWS ONLY;";
        var exception = await ExecuteExpectingFailureAsync(sql);

        Assert.Equal(10744, exception.Number);

        var finding = Assert.Single(Scan(sql));
        Assert.Equal(RowLimitOutOfRangeKind.FetchNotPositive, finding.Kind);
    }

    [Fact]
    public async Task PositiveFetchLiteral_Succeeds_AndScannerDoesNotFlagIt()
    {
        var sql = "SELECT * FROM dbo.T ORDER BY Id OFFSET 0 ROWS FETCH NEXT 1 ROWS ONLY;";
        var exception = await Record.ExceptionAsync(() => ExecuteAsync(sql));

        Assert.Null(exception);
        Assert.Empty(Scan(sql));
    }

    [Fact]
    public async Task TableSamplePercentAbove100_FailsWithMsg476_AndScannerFlagsIt()
    {
        var sql = "SELECT * FROM dbo.T TABLESAMPLE (150 PERCENT);";
        var exception = await ExecuteExpectingFailureAsync(sql);

        Assert.Equal(476, exception.Number);

        var finding = Assert.Single(Scan(sql));
        Assert.Equal(RowLimitOutOfRangeKind.TableSamplePercentOutOfRange, finding.Kind);
    }

    [Fact]
    public async Task TableSampleZeroRows_FailsWithMsg479_AndScannerFlagsIt()
    {
        var sql = "SELECT * FROM dbo.T TABLESAMPLE (0 ROWS);";
        var exception = await ExecuteExpectingFailureAsync(sql);

        Assert.Equal(479, exception.Number);

        var finding = Assert.Single(Scan(sql));
        Assert.Equal(RowLimitOutOfRangeKind.TableSampleRowsNotPositive, finding.Kind);
    }

    [Fact]
    public async Task TableSamplePositiveRows_Succeeds_AndScannerDoesNotFlagIt()
    {
        var sql = "SELECT * FROM dbo.T TABLESAMPLE (100 ROWS);";
        var exception = await Record.ExceptionAsync(() => ExecuteAsync(sql));

        Assert.Null(exception);
        Assert.Empty(Scan(sql));
    }
}
