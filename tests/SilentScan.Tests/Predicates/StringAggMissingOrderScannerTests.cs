using SilentScan.Core.Catalog;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;

namespace SilentScan.Tests.Predicates;

public sealed class StringAggMissingOrderScannerTests
{
    private static IReadOnlyList<StringAggMissingOrderFinding> Scan(string sql)
    {
        var result = SqlScriptParser.ParseText("test.sql", sql);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        return StringAggMissingOrderScanner.Scan(result);
    }

    private static IReadOnlyList<StringAggMissingOrderFinding> ScanWithCatalog(string sql)
    {
        const string Ddl = "CREATE TABLE dbo.T (Id INT NOT NULL PRIMARY KEY, GroupId INT NOT NULL, Name VARCHAR(20) NOT NULL);";
        var result = SqlScriptParser.ParseText("test.sql", Ddl + Environment.NewLine + "GO" + Environment.NewLine + sql);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        return StringAggMissingOrderScanner.Scan(result, CatalogBuilder.Build([result]));
    }

    [Fact]
    public void AggregatedBaseColumn_NamesItsTableAndColumn()
    {
        var finding = Assert.Single(ScanWithCatalog("SELECT STRING_AGG(Name, ',') FROM dbo.T;"));

        Assert.Equal("dbo.T", finding.TableQualifiedName);
        Assert.Equal("Name", finding.ColumnName);
    }

    [Fact]
    public void AggregatedExpression_NamesNoColumn()
    {
        var finding = Assert.Single(ScanWithCatalog("SELECT STRING_AGG(UPPER(Name), ',') FROM dbo.T;"));

        Assert.Null(finding.ColumnName);
    }

    [Fact]
    public void QueryPinnedToOneRowByPrimaryKey_NeverFires()
    {
        Assert.Empty(ScanWithCatalog("SELECT STRING_AGG(Name, ',') FROM dbo.T WHERE Id = 1;"));
    }

    [Theory]
    [InlineData("SELECT STRING_AGG('x', ',') FROM dbo.T;")]
    [InlineData("SELECT STRING_AGG(N'x', N', ') FROM dbo.T GROUP BY GroupId;")]
    [InlineData("DECLARE @v VARCHAR(5) = 'x'; SELECT STRING_AGG(@v, ',') FROM dbo.T;")]
    [InlineData("SELECT STRING_AGG(('x'), ',') FROM dbo.T;")]
    public void AggregatedValueIsAConstant_EveryOrderGivesTheSameString_NeverFires(string sql)
    {
        Assert.Empty(ScanWithCatalog(sql));
    }

    [Fact]
    public void QueryWithoutFromClause_NeverFires()
    {
        Assert.Empty(ScanWithCatalog("SELECT STRING_AGG('a', ',');"));
    }

    [Fact]
    public void QueryOverManyRows_StillFires()
    {
        Assert.Single(ScanWithCatalog("SELECT STRING_AGG(Name, ',') FROM dbo.T;"));
    }

    [Fact]
    public void QueryPinnedOnlyByNonUniqueColumn_StillFires()
    {
        Assert.Single(ScanWithCatalog("SELECT STRING_AGG(Name, ',') FROM dbo.T WHERE GroupId = 1;"));
    }

    [Fact]
    public void StringAgg_NoWithinGroup_Fires()
    {
        var findings = Scan("SELECT STRING_AGG(Name, ',') FROM dbo.T GROUP BY GroupId;");

        Assert.Single(findings);
    }

    [Fact]
    public void StringAgg_WithWithinGroupOrderBy_NeverFires()
    {
        var findings = Scan("SELECT STRING_AGG(Name, ',') WITHIN GROUP (ORDER BY Name) FROM dbo.T GROUP BY GroupId;");

        Assert.Empty(findings);
    }

    [Fact]
    public void OtherAggregate_NeverFires()
    {
        var findings = Scan("SELECT SUM(Amount) FROM dbo.T GROUP BY GroupId;");

        Assert.Empty(findings);
    }

    [Fact]
    public void StringAgg_InsideStoredProcedure_Fires()
    {
        var findings = Scan(
            "CREATE PROCEDURE dbo.usp_Find AS BEGIN SELECT STRING_AGG(Name, ',') FROM dbo.T GROUP BY GroupId; END");

        Assert.Single(findings);
    }

    [Fact]
    public void TwoIndependentStringAggCalls_NoWithinGroup_BothFire()
    {
        var findings = Scan(
            "SELECT STRING_AGG(Name, ',') FROM dbo.T GROUP BY GroupId; SELECT STRING_AGG(City, ';') FROM dbo.U GROUP BY RegionId;");

        Assert.Equal(2, findings.Count);
    }
}
