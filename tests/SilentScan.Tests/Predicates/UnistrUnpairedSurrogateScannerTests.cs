using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;

namespace SilentScan.Tests.Predicates;

public sealed class UnistrUnpairedSurrogateScannerTests
{
    private static IReadOnlyList<UnistrUnpairedSurrogateFinding> Scan(string sql)
    {
        var result = SqlScriptParser.ParseText("test.sql", sql);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        return UnistrUnpairedSurrogateScanner.Scan(result);
    }

    [Fact]
    public void LoneHighSurrogate_Fires()
    {
        var findings = Scan(@"SELECT UNISTR(N'\D800');");

        Assert.Single(findings);
        Assert.Equal(@"\D800", findings[0].EscapeSequence);
    }

    [Fact]
    public void LoneLowSurrogate_Fires()
    {
        var findings = Scan(@"SELECT UNISTR(N'\DC00');");

        Assert.Single(findings);
    }

    [Fact]
    public void ValidSurrogatePair_NeverFires()
    {
        var findings = Scan(@"SELECT UNISTR(N'\D800\DC00');");

        Assert.Empty(findings);
    }

    [Fact]
    public void ReversedSurrogatePair_FiresForBoth()
    {
        var findings = Scan(@"SELECT UNISTR(N'\DC00\D800');");

        Assert.Equal(2, findings.Count);
    }

    [Fact]
    public void WideEscapeInSurrogateRange_Fires()
    {
        var findings = Scan(@"SELECT UNISTR(N'\+00D800');");

        Assert.Single(findings);
    }

    [Fact]
    public void NoEscapes_NeverFires()
    {
        var findings = Scan("SELECT UNISTR(N'hello world');");

        Assert.Empty(findings);
    }

    [Fact]
    public void OrdinaryCodePointEscape_NeverFires()
    {
        var findings = Scan(@"SELECT UNISTR(N'\0041\0042');");

        Assert.Empty(findings);
    }

    [Fact]
    public void NonLiteralArgument_NeverFires()
    {
        var findings = Scan("DECLARE @v NVARCHAR(20) = N'\\D800'; SELECT UNISTR(@v);");

        Assert.Empty(findings);
    }
}
