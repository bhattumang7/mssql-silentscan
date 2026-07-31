using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;

namespace SilentScan.Tests.Predicates;

public sealed class DynamicSqlScannerTests
{
    private static IReadOnlyList<DynamicSqlFinding> Scan(string sql)
    {
        var result = new SqlScriptParser().ParseText("test.sql", sql);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        return DynamicSqlScanner.Scan(result);
    }

    [Fact]
    public void Scan_ExecOfVariable_FlaggedNotLiteral()
    {
        var findings = Scan("DECLARE @sql NVARCHAR(MAX) = N'SELECT 1'; EXEC(@sql);");

        var finding = Assert.Single(findings);
        Assert.False(finding.IsLiteralOnly);
    }

    [Fact]
    public void Scan_ExecOfStringLiteral_FlaggedLiteral()
    {
        var findings = Scan("EXEC('SELECT 1');");

        var finding = Assert.Single(findings);
        Assert.True(finding.IsLiteralOnly);
    }

    [Fact]
    public void Scan_ExecOfConcatenatedExpression_FlaggedNotLiteral()
    {
        var findings = Scan("DECLARE @x NVARCHAR(10) = N'x'; EXEC('SELECT ' + @x);");

        var finding = Assert.Single(findings);
        Assert.False(finding.IsLiteralOnly);
    }

    [Fact]
    public void Scan_SpExecuteSqlWithVariable_FlaggedNotLiteral()
    {
        var findings = Scan("DECLARE @sql NVARCHAR(MAX) = N'SELECT 1'; EXEC sp_executesql @sql;");

        var finding = Assert.Single(findings);
        Assert.False(finding.IsLiteralOnly);
    }

    [Fact]
    public void Scan_SpExecuteSqlWithLiteral_FlaggedLiteral()
    {
        var findings = Scan("EXEC sp_executesql N'SELECT 1';");

        var finding = Assert.Single(findings);
        Assert.True(finding.IsLiteralOnly);
    }

    [Fact]
    public void Scan_NoExecuteStatements_NoFindings()
    {
        var findings = Scan("SELECT 1;");

        Assert.Empty(findings);
    }

    [Fact]
    public void Scan_RegularProcedureExec_NoFinding()
    {
        // EXEC dbo.usp_DoThing is a normal proc call, not dynamic SQL - must not fire.
        var findings = Scan("EXEC dbo.usp_DoThing;");

        Assert.Empty(findings);
    }
}
