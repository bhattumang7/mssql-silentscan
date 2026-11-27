using SilentScan.Core.Catalog;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;

namespace SilentScan.Tests.Predicates;

public sealed class BareTopNoOrderByScannerTests
{
    private static IReadOnlyList<BareTopNoOrderByFinding> Scan(string sql)
    {
        var result = SqlScriptParser.ParseText("test.sql", sql);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        return BareTopNoOrderByScanner.Scan(result);
    }

    [Theory]
    [InlineData("SELECT TOP (2) Name FROM dbo.H ORDER BY (SELECT NULL);", true)]
    [InlineData("DECLARE @x INT = 1; SELECT TOP (2) Name FROM dbo.H ORDER BY @x;", true)]
    [InlineData("DECLARE @x INT = 1; SELECT TOP (2) Name FROM dbo.H ORDER BY @x + 0;", true)]
    [InlineData("SELECT TOP (2) Name FROM dbo.H ORDER BY @@SPID;", true)]
    [InlineData("SELECT TOP (2) Name FROM dbo.H ORDER BY Name;", false)]
    [InlineData("SELECT TOP (2) Name FROM dbo.H ORDER BY 1;", false)]
    [InlineData("DECLARE @k INT = 5; SELECT TOP (1) @k AS Id, Name FROM dbo.H ORDER BY Id;", true)]
    [InlineData("SELECT TOP (1) 7 AS Id, Name FROM dbo.H ORDER BY Id;", true)]
    [InlineData("SELECT TOP (1) 7 AS Id, Name FROM dbo.H ORDER BY H.Id;", false)]
    [InlineData("SELECT TOP (1) Id AS Name, Name AS Other FROM dbo.H ORDER BY Name;", false)]
    [InlineData("DECLARE @k INT = 5; SELECT TOP (1) @k AS Id, @k AS Id FROM dbo.H ORDER BY Id;", false)]
    [InlineData("DECLARE @n INT; SELECT TOP (1) @n = COUNT(*) FROM dbo.H;", false)]
    [InlineData("SELECT TOP (1) ISNULL(MAX(Id), 0) + 1 FROM dbo.H;", false)]
    [InlineData("SELECT TOP (5) COUNT(*), 'x' FROM dbo.H;", false)]
    [InlineData("SELECT TOP (5) Name, COUNT(*) FROM dbo.H GROUP BY Name;", true)]
    public void OrderByOfOnlyRowIndependentKeysOrAnAggregateOnlySelectList(string sql, bool fires)
    {
        Assert.Equal(fires, Scan(sql).Count == 1);
    }

    private const string KeyedDdl = """
        CREATE TABLE dbo.K (Id INT NOT NULL PRIMARY KEY, Name VARCHAR(50) NOT NULL);
        CREATE TABLE dbo.C (A INT NOT NULL, B INT NOT NULL, Name VARCHAR(50) NOT NULL, CONSTRAINT PK_C PRIMARY KEY (A, B));
        CREATE TABLE dbo.H (Id INT NOT NULL, Name VARCHAR(50) NOT NULL);
        CREATE UNIQUE INDEX UX_F ON dbo.H (Id) WHERE Id > 0;
        """;

    private static IReadOnlyList<BareTopNoOrderByFinding> ScanWithCatalog(string sql)
    {
        var ddl = SqlScriptParser.ParseText("ddl.sql", KeyedDdl);
        var catalog = CatalogBuilder.Build([ddl]);
        var result = SqlScriptParser.ParseText("test.sql", sql);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        return BareTopNoOrderByScanner.Scan(result, catalog);
    }

    [Theory]
    [InlineData("SELECT TOP (1) Name FROM dbo.K WHERE Id = 5;")]
    [InlineData("DECLARE @i INT = 5; SELECT TOP (1) Name FROM dbo.K WHERE Id = @i;")]
    [InlineData("SELECT TOP (1) k.Name FROM dbo.K AS k WHERE k.Id = 5 AND k.Name = 'x';")]
    [InlineData("SELECT TOP (3) Name FROM dbo.C WHERE B = 2 AND A = 1;")]
    public void TopOverAnEqualityOnEveryColumnOfAUniqueKey_NeverFires(string sql)
    {
        Assert.Empty(ScanWithCatalog(sql));
    }

    [Theory]
    [InlineData("SELECT TOP (1) Name FROM dbo.K WHERE Id > 5;")]
    [InlineData("SELECT TOP (1) Name FROM dbo.K WHERE Id = 5 OR Id = 6;")]
    [InlineData("SELECT TOP (1) Name FROM dbo.C WHERE A = 1;")]
    [InlineData("SELECT TOP (1) Name FROM dbo.H WHERE Id = 5;")]
    [InlineData("SELECT TOP (1) k.Name FROM dbo.K AS k JOIN dbo.K AS j ON j.Name = k.Name WHERE k.Id = 5;")]
    [InlineData("SELECT TOP (1) Name FROM dbo.K WHERE Id = Id;")]
    public void TopOverAnEqualityThatDoesNotPinAUniqueKey_Fires(string sql)
    {
        Assert.Single(ScanWithCatalog(sql));
    }

    [Fact]
    public void BareTop_NoOrderBy_Fires()
    {
        var findings = Scan("SELECT TOP (5) * FROM dbo.T;");

        Assert.Single(findings);
    }

    [Fact]
    public void BareTop_PlainInteger_NoParens_Fires()
    {
        var findings = Scan("SELECT TOP 5 * FROM dbo.T;");

        Assert.Single(findings);
    }

    [Theory]
    [InlineData("DECLARE @x INT = 4; SELECT TOP (3) @x + 1;")]
    [InlineData("SELECT TOP (2) SYSDATETIME();")]
    public void TopOverASourceWithNoFromClause_NeverFires(string sql)
    {
        Assert.Empty(Scan(sql));
    }

    [Theory]
    [InlineData("SELECT TOP (1) 1 FROM dbo.T;")]
    [InlineData("DECLARE @f INT; SELECT TOP (1) @f = 1 FROM dbo.T;")]
    [InlineData("SELECT TOP (1) 'x' AS c FROM dbo.T;")]
    public void TopWhoseSelectListIsConstant_EveryRowIsIdentical_NeverFires(string sql)
    {
        Assert.Empty(Scan(sql));
    }

    [Fact]
    public void TopWithOrderBy_NeverFires()
    {
        var findings = Scan("SELECT TOP (5) * FROM dbo.T ORDER BY Id;");

        Assert.Empty(findings);
    }

    [Fact]
    public void NoTopAtAll_NeverFires()
    {
        var findings = Scan("SELECT * FROM dbo.T;");

        Assert.Empty(findings);
    }

    [Fact]
    public void TopHundredPercent_NoOrderBy_NeverFires()
    {

        var findings = Scan("SELECT TOP (100) PERCENT * FROM dbo.T;");

        Assert.Empty(findings);
    }

    [Fact]
    public void TopNinetyNinePercent_NoOrderBy_Fires()
    {

        var findings = Scan("SELECT TOP (99) PERCENT * FROM dbo.T;");

        Assert.Single(findings);
    }

    [Fact]
    public void TopHundredPointZeroPercent_NoOrderBy_NeverFires()
    {
        var findings = Scan("SELECT TOP (100.0) PERCENT * FROM dbo.T;");

        Assert.Empty(findings);
    }

    [Fact]
    public void TopHundredPointZeroZeroPercent_NoOrderBy_NeverFires()
    {
        var findings = Scan("SELECT TOP 100.00 PERCENT * FROM dbo.T;");

        Assert.Empty(findings);
    }

    [Fact]
    public void TopNinetyNinePointFivePercent_NoOrderBy_Fires()
    {
        var findings = Scan("SELECT TOP (99.5) PERCENT * FROM dbo.T;");

        Assert.Single(findings);
    }

    [Fact]
    public void TopWithTies_AlwaysCarriesOrderBy_NeverFires()
    {

        var findings = Scan("SELECT TOP (5) WITH TIES * FROM dbo.T ORDER BY Id;");

        Assert.Empty(findings);
    }

    [Fact]
    public void NestedSubqueryTop_NoOrderBy_FiresIndependently()
    {
        var findings = Scan(
            "SELECT * FROM (SELECT TOP (3) * FROM dbo.T) AS sub;");

        Assert.Single(findings);
    }

    [Fact]
    public void BareTop_InsideStoredProcedure_Fires()
    {
        var findings = Scan(
            "CREATE PROCEDURE dbo.usp_Find AS BEGIN SELECT TOP (10) Id FROM dbo.T; END");

        Assert.Single(findings);
    }

    [Fact]
    public void BareTop_InsideView_OutermostQuery_Fires()
    {

        var findings = Scan("CREATE VIEW dbo.V AS SELECT TOP (10) Id FROM dbo.T;");

        Assert.Single(findings);
    }

    [Fact]
    public void TwoIndependentBareTops_BothFire()
    {
        var findings = Scan(
            "SELECT TOP (5) * FROM dbo.T; SELECT TOP (3) * FROM dbo.U;");

        Assert.Equal(2, findings.Count);
    }

    [Fact]
    public void TopZero_NeverFires()
    {
        var findings = Scan("SELECT TOP (0) a FROM dbo.T;");

        Assert.Empty(findings);
    }

    [Fact]
    public void TopWithConstantOnlySelectList_NeverFires()
    {
        var findings = Scan("SELECT TOP (1) 1 FROM dbo.T;");

        Assert.Empty(findings);
    }

    [Fact]
    public void TopOverAggregateWithoutGroupBy_NeverFires()
    {
        var findings = Scan("SELECT TOP (1) MAX(a) FROM dbo.T;");

        Assert.Empty(findings);
    }

    [Fact]
    public void TopInsideExistsSubquery_NeverFires()
    {
        var findings = Scan("SELECT a FROM dbo.T WHERE EXISTS (SELECT TOP (1) b FROM dbo.U);");

        Assert.Empty(findings);
    }

    [Fact]
    public void TopInsideNotExistsInIfStatement_NeverFires()
    {
        var findings = Scan("IF NOT EXISTS (SELECT TOP (1) b FROM dbo.U) PRINT 'none';");

        Assert.Empty(findings);
    }

    [Fact]
    public void TopInsideScalarSubquery_StillFires()
    {
        var findings = Scan("SELECT (SELECT TOP (1) b FROM dbo.U) FROM dbo.T;");

        Assert.Single(findings);
    }

    [Fact]
    public void TopOverAggregateWithGroupBy_StillFires()
    {
        var findings = Scan("SELECT TOP (1) MAX(a) FROM dbo.T GROUP BY b;");

        Assert.Single(findings);
    }

    [Theory]
    [InlineData("SELECT TOP (5) v.x FROM (VALUES (1)) AS v(x);", false)]
    [InlineData("SELECT TOP (5) d.x FROM (SELECT 1 AS x) AS d;", false)]
    [InlineData("SELECT TOP (5) d.c FROM (SELECT COUNT(*) AS c FROM dbo.H) AS d;", false)]
    [InlineData("SELECT TOP (1) v.x FROM (VALUES (1), (2)) AS v(x);", true)]
    [InlineData("SELECT TOP (1) d.x FROM (SELECT Id AS x FROM dbo.H) AS d;", true)]
    [InlineData("SELECT TOP (1) d.x FROM (SELECT 1 AS x UNION ALL SELECT 2) AS d;", true)]
    public void TopOverASourceOfAtMostOneRow_NeverFires(string sql, bool fires)
    {
        Assert.Equal(fires, Scan(sql).Count == 1);
    }

    [Theory]
    [InlineData("SELECT TOP (5) a.R FROM dbo.U AS a JOIN dbo.U2 AS b ON b.Id = a.R WHERE a.Id = 1;", false)]
    [InlineData("SELECT TOP (5) R FROM dbo.U WHERE Id IN (1);", false)]
    [InlineData("SELECT TOP (5) R FROM dbo.USyn WHERE Id = 1;", false)]
    [InlineData("SELECT TOP (5) x.R FROM (SELECT TOP (1) R FROM dbo.U ORDER BY R) AS x;", false)]
    [InlineData("SELECT TOP (5) x.R FROM (SELECT R FROM dbo.U WHERE Id = 1) AS x;", false)]
    [InlineData("SELECT TOP (5) x.R FROM (SELECT TOP (1) WITH TIES R FROM dbo.U ORDER BY R) AS x;", true)]
    [InlineData("SELECT TOP (5) R FROM dbo.U WHERE R = 10;", true)]
    [InlineData("SELECT TOP (5) a.R FROM dbo.U AS a LEFT JOIN dbo.U2 AS b ON a.Id = 1 AND b.Id = 10;", true)]
    [InlineData("SELECT TOP (5) a.R FROM dbo.U AS a LEFT JOIN dbo.U2 AS b ON b.Id = a.R WHERE a.Id = 1;", false)]
    [InlineData("WITH U AS (SELECT R AS Id, R FROM dbo.U) SELECT TOP (5) R FROM U WHERE Id = 1;", true)]
    public void RowSourcesPinnedToOneRow_NeverFire(string statement, bool fires)
    {
        var result = SqlScriptParser.ParseText(
            "test.sql",
            "CREATE TABLE dbo.U (Id INT NOT NULL PRIMARY KEY, R INT NOT NULL);\n"
            + "CREATE TABLE dbo.U2 (Id INT NOT NULL PRIMARY KEY, R INT NOT NULL);\n"
            + "CREATE SYNONYM dbo.USyn FOR dbo.U;\nGO\n" + statement);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        var catalog = SilentScan.Core.Catalog.CatalogBuilder.Build([result]);

        Assert.Equal(fires, BareTopNoOrderByScanner.Scan(result, catalog).Count > 0);
    }
}
