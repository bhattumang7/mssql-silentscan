using Microsoft.Data.SqlClient;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class GroupByValidityOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(GroupByValidityOracleTests);

    protected override string Ddl => """
        CREATE TABLE dbo.Sale (Id INT NOT NULL PRIMARY KEY, Category NVARCHAR(20) NOT NULL, SubCategory NVARCHAR(20) NOT NULL, Amount INT NOT NULL);
        INSERT INTO dbo.Sale VALUES (1, 'a', 'x', 10), (2, 'b', 'y', 20);
        """;

    private static IReadOnlyList<GroupByValidityFinding> Scan(string sql)
    {
        var result = SqlScriptParser.ParseText("test.sql", sql);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        return GroupByValidityScanner.Scan(result);
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
    public async Task BareUngroupedColumn_FailsWithMsg8120_AndScannerFlagsIt()
    {
        var sql = "SELECT Id, Category, SUM(Amount) FROM dbo.Sale GROUP BY Category;";
        var exception = await ExecuteExpectingFailureAsync(sql);

        Assert.Equal(8120, exception.Number);
        Assert.Contains(Scan(sql), f => f.Kind == GroupByValidityFindingKind.SelectList);
    }

    [Fact]
    public async Task GroupByFullPrimaryKey_StillFailsWithMsg8120_AndScannerFlagsIt()
    {
        var sql = "SELECT Id, Category FROM dbo.Sale GROUP BY Id;";
        var exception = await ExecuteExpectingFailureAsync(sql);

        Assert.Equal(8120, exception.Number);
        Assert.Contains(Scan(sql), f => f.Kind == GroupByValidityFindingKind.SelectList);
    }

    [Fact]
    public async Task ColumnDifferingOnlyByCase_Succeeds_AndScannerDoesNotFlagIt()
    {
        var sql = "SELECT CATEGORY FROM dbo.Sale GROUP BY Category;";
        var exception = await Record.ExceptionAsync(() => ExecuteAsync(sql));

        Assert.Null(exception);
        Assert.Empty(Scan(sql));
    }

    [Fact]
    public async Task ColumnQualifiedDifferentlyThanGroupBy_Succeeds_AndScannerDoesNotFlagIt()
    {
        var sql = "SELECT Sale.Category FROM dbo.Sale GROUP BY Category;";
        var exception = await Record.ExceptionAsync(() => ExecuteAsync(sql));

        Assert.Null(exception);
        Assert.Empty(Scan(sql));
    }

    [Fact]
    public async Task RollupColumnNotInArguments_FailsWithMsg8120_AndScannerFlagsIt()
    {
        var sql = "SELECT Id, Category, SUM(Amount) FROM dbo.Sale GROUP BY ROLLUP(Category);";
        var exception = await ExecuteExpectingFailureAsync(sql);

        Assert.Equal(8120, exception.Number);
        Assert.Contains(Scan(sql), f => f.Kind == GroupByValidityFindingKind.SelectList);
    }

    [Fact]
    public async Task RollupColumnInArguments_Succeeds_AndScannerDoesNotFlagIt()
    {
        var sql = "SELECT Category, SubCategory, SUM(Amount) FROM dbo.Sale GROUP BY ROLLUP(Category, SubCategory);";
        var exception = await Record.ExceptionAsync(() => ExecuteAsync(sql));

        Assert.Null(exception);
        Assert.Empty(Scan(sql));
    }

    [Fact]
    public async Task CubeColumnInArguments_Succeeds_AndScannerDoesNotFlagIt()
    {
        var sql = "SELECT Category, SubCategory FROM dbo.Sale GROUP BY CUBE(Category, SubCategory);";
        var exception = await Record.ExceptionAsync(() => ExecuteAsync(sql));

        Assert.Null(exception);
        Assert.Empty(Scan(sql));
    }

    [Fact]
    public async Task GroupingSetsColumnNotInAnySet_FailsWithMsg8120_AndScannerFlagsIt()
    {
        var sql = "SELECT Id, Category, SUM(Amount) FROM dbo.Sale GROUP BY GROUPING SETS (Category, ());";
        var exception = await ExecuteExpectingFailureAsync(sql);

        Assert.Equal(8120, exception.Number);
        Assert.Contains(Scan(sql), f => f.Kind == GroupByValidityFindingKind.SelectList);
    }

    [Fact]
    public async Task GroupingSetsColumnAppearsInOneSet_Succeeds_AndScannerDoesNotFlagIt()
    {
        var sql = "SELECT Category, Id, SUM(Amount) FROM dbo.Sale GROUP BY GROUPING SETS (Category, (Id));";
        var exception = await Record.ExceptionAsync(() => ExecuteAsync(sql));

        Assert.Null(exception);
        Assert.Empty(Scan(sql));
    }

    [Fact]
    public async Task WindowAggregateOverUngroupedColumn_FailsWithMsg8120_AndScannerFlagsIt()
    {
        var sql = "SELECT Category, SUM(Amount) OVER (PARTITION BY Category) FROM dbo.Sale GROUP BY Category;";
        var exception = await ExecuteExpectingFailureAsync(sql);

        Assert.Equal(8120, exception.Number);
        Assert.Contains(Scan(sql), f => f.Kind == GroupByValidityFindingKind.SelectList);
    }

    [Fact]
    public async Task WindowAggregateOverNestedRealAggregate_Succeeds_AndScannerDoesNotFlagIt()
    {
        var sql = "SELECT Category, SUM(SUM(Amount)) OVER (PARTITION BY Category) FROM dbo.Sale GROUP BY Category;";
        var exception = await Record.ExceptionAsync(() => ExecuteAsync(sql));

        Assert.Null(exception);
        Assert.Empty(Scan(sql));
    }

    [Fact]
    public async Task WindowFunctionWithNoArguments_Succeeds_AndScannerDoesNotFlagIt()
    {
        var sql = "SELECT Category, SUM(Amount), ROW_NUMBER() OVER (ORDER BY Category) FROM dbo.Sale GROUP BY Category;";
        var exception = await Record.ExceptionAsync(() => ExecuteAsync(sql));

        Assert.Null(exception);
        Assert.Empty(Scan(sql));
    }
}
