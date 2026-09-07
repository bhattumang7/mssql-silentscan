using SilentScan.Core.Catalog;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;

namespace SilentScan.Tests.Predicates;

public sealed class RegexpDefaultCaseSensitiveOnCiColumnScannerTests
{
    private static IReadOnlyList<RegexpDefaultCaseSensitiveOnCiColumnFinding> Scan(string ddl, string sql)
    {
        var ddlResult = SqlScriptParser.ParseText("schema.sql", ddl);
        Assert.False(ddlResult.HasErrors, string.Join("; ", ddlResult.Errors.Select(e => e.Message)));
        var catalog = CatalogBuilder.Build([ddlResult]);

        var result = SqlScriptParser.ParseText("test.sql", sql);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        return RegexpDefaultCaseSensitiveOnCiColumnScanner.Scan(result, catalog);
    }

    private const string CiTable = "CREATE TABLE dbo.T (Name VARCHAR(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL);";
    private const string CsTable = "CREATE TABLE dbo.T (Name VARCHAR(50) COLLATE SQL_Latin1_General_CP1_CS_AS NOT NULL);";

    [Fact]
    public void RegexpLike_NoFlags_CiColumn_Fires()
    {
        var findings = Scan(CiTable, "SELECT * FROM dbo.T WHERE REGEXP_LIKE(Name, '[Jj]ohn');");

        var finding = Assert.Single(findings);
        Assert.Equal("REGEXP_LIKE", finding.FunctionName);
        Assert.Equal("Name", finding.ColumnName);
    }

    [Fact]
    public void RegexpLike_ExplicitIFlag_NeverFires()
    {
        var findings = Scan(CiTable, "SELECT * FROM dbo.T WHERE REGEXP_LIKE(Name, 'john', 'i');");

        Assert.Empty(findings);
    }

    [Fact]
    public void RegexpLike_FlagsEndingInC_Fires()
    {
        var findings = Scan(CiTable, "SELECT * FROM dbo.T WHERE REGEXP_LIKE(Name, 'john', 'ic');");

        Assert.Single(findings);
    }

    [Fact]
    public void RegexpLike_FlagsEndingInI_NeverFires()
    {
        var findings = Scan(CiTable, "SELECT * FROM dbo.T WHERE REGEXP_LIKE(Name, 'john', 'sci');");

        Assert.Empty(findings);
    }

    [Fact]
    public void RegexpLike_CsColumn_NeverFires()
    {
        var findings = Scan(CsTable, "SELECT * FROM dbo.T WHERE REGEXP_LIKE(Name, 'john');");

        Assert.Empty(findings);
    }

    [Fact]
    public void RegexpLike_PatternWithNoLetters_NeverFires()
    {
        var findings = Scan(CiTable, "SELECT * FROM dbo.T WHERE REGEXP_LIKE(Name, '[0-9]+');");

        Assert.Empty(findings);
    }

    [Fact]
    public void RegexpLike_NonLiteralFlags_NeverFires()
    {
        var findings = Scan(CiTable, "DECLARE @f VARCHAR(5) = 'i'; SELECT * FROM dbo.T WHERE REGEXP_LIKE(Name, 'john', @f);");

        Assert.Empty(findings);
    }

    [Fact]
    public void RegexpReplace_NoFlags_CiColumn_Fires()
    {
        var findings = Scan(CiTable, "SELECT REGEXP_REPLACE(Name, 'john', 'x') FROM dbo.T;");

        var finding = Assert.Single(findings);
        Assert.Equal("REGEXP_REPLACE", finding.FunctionName);
    }

    [Fact]
    public void RegexpReplace_WithIFlag_NeverFires()
    {
        var findings = Scan(CiTable, "SELECT REGEXP_REPLACE(Name, 'john', 'x', 1, 0, 'i') FROM dbo.T;");

        Assert.Empty(findings);
    }

    [Fact]
    public void RegexpCount_NoFlags_CiColumn_Fires()
    {
        var findings = Scan(CiTable, "SELECT REGEXP_COUNT(Name, 'john') FROM dbo.T;");

        var finding = Assert.Single(findings);
        Assert.Equal("REGEXP_COUNT", finding.FunctionName);
    }

    [Fact]
    public void RegexpCount_WithIFlag_NeverFires()
    {
        var findings = Scan(CiTable, "SELECT REGEXP_COUNT(Name, 'john', 1, 'i') FROM dbo.T;");

        Assert.Empty(findings);
    }

    [Fact]
    public void RegexpSubstr_NoFlags_CiColumn_Fires()
    {
        var findings = Scan(CiTable, "SELECT REGEXP_SUBSTR(Name, 'john') FROM dbo.T;");

        var finding = Assert.Single(findings);
        Assert.Equal("REGEXP_SUBSTR", finding.FunctionName);
    }

    [Fact]
    public void RegexpSubstr_WithIFlag_NeverFires()
    {
        var findings = Scan(CiTable, "SELECT REGEXP_SUBSTR(Name, 'john', 1, 1, 'i') FROM dbo.T;");

        Assert.Empty(findings);
    }

    [Fact]
    public void RegexpSubstr_WithIFlagAndGroup_NeverFires()
    {
        var findings = Scan(CiTable, "SELECT REGEXP_SUBSTR(Name, '(john)', 1, 1, 'i', 1) FROM dbo.T;");

        Assert.Empty(findings);
    }

    [Fact]
    public void NonRegexpFunction_NeverFires()
    {
        var findings = Scan(CiTable, "SELECT UPPER(Name) FROM dbo.T;");

        Assert.Empty(findings);
    }
}
