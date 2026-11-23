using SilentScan.Core.Catalog;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;

namespace SilentScan.Tests.Predicates;

public sealed class JsonArrayAggMissingOrderScannerTests
{
    private static IReadOnlyList<JsonArrayAggMissingOrderFinding> Scan(string sql)
    {
        var result = SqlScriptParser.ParseText("test.sql", sql);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        return JsonArrayAggMissingOrderScanner.Scan(result);
    }

    private static IReadOnlyList<JsonArrayAggMissingOrderFinding> ScanWithCatalog(string sql)
    {
        const string Ddl = "CREATE TABLE dbo.T (Id INT NOT NULL PRIMARY KEY, GroupId INT NOT NULL, Name VARCHAR(20) NOT NULL);";
        var result = SqlScriptParser.ParseText("test.sql", Ddl + Environment.NewLine + "GO" + Environment.NewLine + sql);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        return JsonArrayAggMissingOrderScanner.Scan(result, CatalogBuilder.Build([result]));
    }

    [Theory]
    [InlineData("SELECT JSON_ARRAYAGG('x') FROM dbo.T;", false)]
    [InlineData("SELECT JSON_ARRAYAGG(1) FROM dbo.T;", false)]
    [InlineData("DECLARE @v INT = 1; SELECT JSON_ARRAYAGG(@v) FROM dbo.T;", false)]
    [InlineData("SELECT JSON_ARRAYAGG(Name) FROM dbo.T;", true)]
    [InlineData("SELECT JSON_ARRAYAGG(Id + 1) FROM dbo.T;", true)]
    public void ConstantElement_HasNoOrderToLose_OnlyRowDependentElementsFire(string sql, bool fires)
    {
        Assert.Equal(fires, ScanWithCatalog(sql).Count == 1);
    }

    [Fact]
    public void AggregatedBaseColumn_NamesItsTableAndColumn()
    {
        var finding = Assert.Single(ScanWithCatalog("SELECT JSON_ARRAYAGG(Name) FROM dbo.T;"));

        Assert.Equal("dbo.T", finding.TableQualifiedName);
        Assert.Equal("Name", finding.ColumnName);
    }

    [Fact]
    public void QueryPinnedToOneRowByPrimaryKey_NeverFires()
    {
        Assert.Empty(ScanWithCatalog("SELECT JSON_ARRAYAGG(Name) FROM dbo.T WHERE Id = 1;"));
    }

    [Fact]
    public void AggregatedValueIsAConstant_EveryOrderGivesTheSameArray_NeverFires()
    {
        Assert.Empty(ScanWithCatalog("SELECT JSON_ARRAYAGG('x') FROM dbo.T;"));
    }

    [Fact]
    public void QueryWithoutFromClause_NeverFires()
    {
        Assert.Empty(ScanWithCatalog("SELECT JSON_ARRAYAGG('a');"));
    }

    [Fact]
    public void QueryOverManyRows_StillFires()
    {
        Assert.Single(ScanWithCatalog("SELECT JSON_ARRAYAGG(Name) FROM dbo.T;"));
    }

    [Fact]
    public void QueryPinnedOnlyByNonUniqueColumn_StillFires()
    {
        Assert.Single(ScanWithCatalog("SELECT JSON_ARRAYAGG(Name) FROM dbo.T WHERE GroupId = 1;"));
    }

    [Fact]
    public void JsonArrayAgg_NoOrder_Fires()
    {
        var findings = Scan("SELECT JSON_ARRAYAGG(Name) FROM dbo.T GROUP BY GroupId;");

        Assert.Single(findings);
    }

    [Fact]
    public void JsonArrayAgg_WithOwnOrderBy_NeverFires()
    {
        var findings = Scan("SELECT JSON_ARRAYAGG(Name ORDER BY Name) FROM dbo.T GROUP BY GroupId;");

        Assert.Empty(findings);
    }

    [Fact]
    public void OtherAggregate_NeverFires()
    {
        var findings = Scan("SELECT STRING_AGG(Name, ',') WITHIN GROUP (ORDER BY Name) FROM dbo.T GROUP BY GroupId;");

        Assert.Empty(findings);
    }

    [Fact]
    public void JsonArrayAgg_InsideStoredProcedure_Fires()
    {
        var findings = Scan(
            "CREATE PROCEDURE dbo.usp_Find AS BEGIN SELECT JSON_ARRAYAGG(Name) FROM dbo.T GROUP BY GroupId; END");

        Assert.Single(findings);
    }
}
