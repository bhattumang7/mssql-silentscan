using SilentScan.Core.Catalog;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;

namespace SilentScan.Tests.Predicates;

public sealed class RegexpAccentInsensitiveColumnScannerTests
{
    private static IReadOnlyList<RegexpAccentInsensitiveColumnFinding> Scan(string ddl, string sql)
    {
        var ddlResult = SqlScriptParser.ParseText("schema.sql", ddl);
        Assert.False(ddlResult.HasErrors, string.Join("; ", ddlResult.Errors.Select(e => e.Message)));
        var catalog = CatalogBuilder.Build([ddlResult]);

        var result = SqlScriptParser.ParseText("test.sql", sql);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        return RegexpAccentInsensitiveColumnScanner.Scan(result, catalog);
    }

    private const string AiTable = "CREATE TABLE dbo.T (Name VARCHAR(50) COLLATE SQL_Latin1_General_CP1_CI_AI NOT NULL);";
    private const string AsTable = "CREATE TABLE dbo.T (Name VARCHAR(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL);";

    [Fact]
    public void RegexpLike_AiColumn_Fires()
    {
        var findings = Scan(AiTable, "SELECT * FROM dbo.T WHERE REGEXP_LIKE(Name, 'cafe');");

        var finding = Assert.Single(findings);
        Assert.Equal("REGEXP_LIKE", finding.FunctionName);
        Assert.Equal("Name", finding.ColumnName);
    }

    [Fact]
    public void RegexpLike_AiColumn_WithIFlag_StillFires()
    {
        var findings = Scan(AiTable, "SELECT * FROM dbo.T WHERE REGEXP_LIKE(Name, 'cafe', 'i');");

        Assert.Single(findings);
    }

    [Fact]
    public void RegexpLike_AsColumn_NeverFires()
    {
        var findings = Scan(AsTable, "SELECT * FROM dbo.T WHERE REGEXP_LIKE(Name, 'cafe');");

        Assert.Empty(findings);
    }

    [Fact]
    public void RegexpLike_PatternWithNoLetters_NeverFires()
    {
        var findings = Scan(AiTable, "SELECT * FROM dbo.T WHERE REGEXP_LIKE(Name, '[0-9]+');");

        Assert.Empty(findings);
    }

    [Fact]
    public void RegexpReplace_AiColumn_Fires()
    {
        var findings = Scan(AiTable, "SELECT REGEXP_REPLACE(Name, 'cafe', 'x') FROM dbo.T;");

        var finding = Assert.Single(findings);
        Assert.Equal("REGEXP_REPLACE", finding.FunctionName);
    }

    [Fact]
    public void RegexpCount_AiColumn_Fires()
    {
        var findings = Scan(AiTable, "SELECT REGEXP_COUNT(Name, 'cafe') FROM dbo.T;");

        var finding = Assert.Single(findings);
        Assert.Equal("REGEXP_COUNT", finding.FunctionName);
    }

    [Fact]
    public void RegexpSubstr_AiColumn_Fires()
    {
        var findings = Scan(AiTable, "SELECT REGEXP_SUBSTR(Name, 'cafe') FROM dbo.T;");

        var finding = Assert.Single(findings);
        Assert.Equal("REGEXP_SUBSTR", finding.FunctionName);
    }

    [Fact]
    public void RegexpMatches_AiColumn_Fires()
    {
        var findings = Scan(AiTable, "SELECT m.* FROM dbo.T CROSS APPLY REGEXP_MATCHES(Name, 'cafe') AS m;");

        var finding = Assert.Single(findings);
        Assert.Equal("REGEXP_MATCHES", finding.FunctionName);
    }

    [Fact]
    public void RegexpSplitToTable_AiColumn_Fires()
    {
        var findings = Scan(AiTable, "SELECT s.* FROM dbo.T CROSS APPLY REGEXP_SPLIT_TO_TABLE(Name, 'cafe') AS s;");

        var finding = Assert.Single(findings);
        Assert.Equal("REGEXP_SPLIT_TO_TABLE", finding.FunctionName);
    }

    [Fact]
    public void NonRegexpFunction_NeverFires()
    {
        var findings = Scan(AiTable, "SELECT UPPER(Name) FROM dbo.T;");

        Assert.Empty(findings);
    }
}
