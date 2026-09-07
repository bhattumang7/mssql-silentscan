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
