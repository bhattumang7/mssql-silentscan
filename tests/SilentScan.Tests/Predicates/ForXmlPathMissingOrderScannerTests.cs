using SilentScan.Core.Catalog;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;

namespace SilentScan.Tests.Predicates;

public sealed class ForXmlPathMissingOrderScannerTests
{
    private static IReadOnlyList<ForXmlPathMissingOrderFinding> Scan(string sql)
    {
        var result = SqlScriptParser.ParseText("test.sql", sql);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        return ForXmlPathMissingOrderScanner.Scan(result);
    }

    private static IReadOnlyList<ForXmlPathMissingOrderFinding> ScanWithCatalog(string sql)
    {
        const string Ddl = "CREATE TABLE dbo.T (Id INT NOT NULL PRIMARY KEY, GroupId INT NOT NULL, Name VARCHAR(20) NOT NULL);";
        var result = SqlScriptParser.ParseText("test.sql", Ddl + Environment.NewLine + "GO" + Environment.NewLine + sql);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        return ForXmlPathMissingOrderScanner.Scan(result, CatalogBuilder.Build([result]));
    }

    [Theory]
    [InlineData("SELECT Name FROM dbo.T ORDER BY (SELECT NULL) FOR XML PATH('');", true)]
    [InlineData("DECLARE @x INT = 1; SELECT Name FROM dbo.T ORDER BY @x FOR XML PATH('');", true)]
    [InlineData("SELECT Name FROM dbo.T ORDER BY Id FOR XML PATH('');", false)]
    public void OrderByOfOnlyRowIndependentKeys_HasNoOrder(string sql, bool fires)
    {
        Assert.Equal(fires, ScanWithCatalog(sql).Count == 1);
    }

    [Theory]
    [InlineData("SELECT ',' FROM dbo.T FOR XML PATH('');", false)]
    [InlineData("DECLARE @v VARCHAR(5) = 'x'; SELECT @v FROM dbo.T FOR XML PATH('');", false)]
    [InlineData("DECLARE @v VARCHAR(5) = 'x'; SELECT ',' + @v FROM dbo.T FOR XML PATH('');", false)]
    [InlineData("SELECT Name FROM dbo.T FOR XML PATH('');", true)]
    [InlineData("SELECT ',' + Name FROM dbo.T FOR XML PATH('');", true)]
    [InlineData("SELECT ',', Name FROM dbo.T FOR XML PATH('');", true)]
    public void SelectListOfOnlyRowIndependentValues_HasNoOrderToLose(string sql, bool fires)
    {
        Assert.Equal(fires, ScanWithCatalog(sql).Count == 1);
    }

    [Fact]
    public void QueryPinnedToOneRowByPrimaryKey_NeverFires()
    {
        Assert.Empty(ScanWithCatalog("SELECT Name FROM dbo.T WHERE Id = 1 FOR XML PATH('r');"));
    }

    [Fact]
    public void QueryWithoutFromClause_NeverFires()
    {
        Assert.Empty(ScanWithCatalog("SELECT 'a' FOR XML PATH('r');"));
    }

    [Fact]
    public void QueryOverManyRows_StillFires()
    {
        Assert.Single(ScanWithCatalog("SELECT Name FROM dbo.T FOR XML PATH('r');"));
    }

    [Fact]
    public void QueryPinnedOnlyByNonUniqueColumn_StillFires()
    {
        Assert.Single(ScanWithCatalog("SELECT Name FROM dbo.T WHERE GroupId = 1 FOR XML PATH('r');"));
    }

    [Fact]
    public void ForXmlPath_NoOrderBy_Fires()
    {
        var findings = Scan(
            "SELECT STUFF((SELECT ',' + Name FROM dbo.T FOR XML PATH('')), 1, 1, '');");

        Assert.Single(findings);
    }

    [Fact]
    public void ForXmlPath_WithOrderBy_NeverFires()
    {
        var findings = Scan(
            "SELECT STUFF((SELECT ',' + Name FROM dbo.T ORDER BY Name FOR XML PATH('')), 1, 1, '');");

        Assert.Empty(findings);
    }

    [Fact]
    public void ForXmlAuto_NoOrderBy_NeverFires()
    {
        var findings = Scan("SELECT Name FROM dbo.T FOR XML AUTO;");

        Assert.Empty(findings);
    }

    [Fact]
    public void ForXmlRaw_NoOrderBy_NeverFires()
    {
        var findings = Scan("SELECT Name FROM dbo.T FOR XML RAW;");

        Assert.Empty(findings);
    }

    [Fact]
    public void NoForXmlAtAll_NeverFires()
    {
        var findings = Scan("SELECT Name FROM dbo.T;");

        Assert.Empty(findings);
    }

    [Fact]
    public void TwoIndependentForXmlPath_NoOrderBy_BothFire()
    {
        var findings = Scan(
            "SELECT STUFF((SELECT ',' + Name FROM dbo.T FOR XML PATH('')), 1, 1, ''); "
            + "SELECT STUFF((SELECT ';' + City FROM dbo.U FOR XML PATH('')), 1, 1, '');");

        Assert.Equal(2, findings.Count);
    }
}
