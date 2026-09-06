using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;

namespace SilentScan.Tests.Predicates;

public sealed class RowLimitOutOfRangeScannerTests
{
    private const string Ddl = "CREATE TABLE dbo.T (Id INT NOT NULL PRIMARY KEY, V INT NOT NULL);";

    private static IReadOnlyList<RowLimitOutOfRangeFinding> Scan(string sql)
    {
        var result = SqlScriptParser.ParseText("test.sql", $"{Ddl}\n{sql}");
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        return RowLimitOutOfRangeScanner.Scan(result);
    }

    [Fact]
    public void TopNegativeLiteral_Fires()
    {
        var finding = Assert.Single(Scan("SELECT TOP (-1) * FROM dbo.T;"));
        Assert.Equal(RowLimitOutOfRangeKind.TopRowCountNegative, finding.Kind);
        Assert.Equal("-1", finding.LiteralValueDisplay);
    }

    [Fact]
    public void TopZeroLiteral_DoesNotFire()
    {
        Assert.Empty(Scan("SELECT TOP (0) * FROM dbo.T;"));
    }

    [Fact]
    public void TopPositiveLiteral_DoesNotFire()
    {
        Assert.Empty(Scan("SELECT TOP (10) * FROM dbo.T;"));
    }

    [Fact]
    public void TopVariable_DoesNotFire()
    {
        Assert.Empty(Scan("DECLARE @n INT = -1; SELECT TOP (@n) * FROM dbo.T;"));
    }

    [Fact]
    public void OffsetNegativeLiteral_Fires()
    {
        var finding = Assert.Single(Scan("SELECT * FROM dbo.T ORDER BY Id OFFSET -1 ROWS;"));
        Assert.Equal(RowLimitOutOfRangeKind.OffsetNegative, finding.Kind);
    }

    [Fact]
    public void OffsetZeroLiteral_DoesNotFire()
    {
        Assert.Empty(Scan("SELECT * FROM dbo.T ORDER BY Id OFFSET 0 ROWS;"));
    }

    [Theory]
    [InlineData("SELECT * FROM dbo.T ORDER BY Id OFFSET 0 ROWS FETCH NEXT -1 ROWS ONLY;")]
    [InlineData("SELECT * FROM dbo.T ORDER BY Id OFFSET 0 ROWS FETCH NEXT 0 ROWS ONLY;")]
    public void FetchNotPositive_Fires(string sql)
    {
        var finding = Assert.Single(Scan(sql));
        Assert.Equal(RowLimitOutOfRangeKind.FetchNotPositive, finding.Kind);
    }

    [Fact]
    public void FetchPositiveLiteral_DoesNotFire()
    {
        Assert.Empty(Scan("SELECT * FROM dbo.T ORDER BY Id OFFSET 0 ROWS FETCH NEXT 1 ROWS ONLY;"));
    }

    [Theory]
    [InlineData("SELECT * FROM dbo.T TABLESAMPLE (150 PERCENT);")]
    [InlineData("SELECT * FROM dbo.T TABLESAMPLE (-5 PERCENT);")]
    public void TableSamplePercentOutOfRange_Fires(string sql)
    {
        var finding = Assert.Single(Scan(sql));
        Assert.Equal(RowLimitOutOfRangeKind.TableSamplePercentOutOfRange, finding.Kind);
    }

    [Fact]
    public void TableSamplePercentInRange_DoesNotFire()
    {
        Assert.Empty(Scan("SELECT * FROM dbo.T TABLESAMPLE (10 PERCENT);"));
    }

    [Theory]
    [InlineData("SELECT * FROM dbo.T TABLESAMPLE (-5 ROWS);")]
    [InlineData("SELECT * FROM dbo.T TABLESAMPLE (0 ROWS);")]
    public void TableSampleRowsNotPositive_Fires(string sql)
    {
        var finding = Assert.Single(Scan(sql));
        Assert.Equal(RowLimitOutOfRangeKind.TableSampleRowsNotPositive, finding.Kind);
    }

    [Fact]
    public void TableSampleRowsPositive_DoesNotFire()
    {
        Assert.Empty(Scan("SELECT * FROM dbo.T TABLESAMPLE (100 ROWS);"));
    }
}
