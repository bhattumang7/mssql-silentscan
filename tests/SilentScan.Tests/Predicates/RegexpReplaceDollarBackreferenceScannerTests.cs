using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;

namespace SilentScan.Tests.Predicates;

public sealed class RegexpReplaceDollarBackreferenceScannerTests
{
    private static IReadOnlyList<RegexpReplaceDollarBackreferenceFinding> Scan(string sql)
    {
        var result = SqlScriptParser.ParseText("test.sql", sql);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        return RegexpReplaceDollarBackreferenceScanner.Scan(result);
    }

    [Fact]
    public void DollarBackreference_WithinCaptureGroupCount_Fires()
    {
        var findings = Scan("SELECT REGEXP_REPLACE('abc123def', '([a-z]+)([0-9]+)', '$2-$1');");

        Assert.Equal(2, findings.Count);
        Assert.Contains(findings, f => f.DollarToken == "$1");
        Assert.Contains(findings, f => f.DollarToken == "$2");
    }

    [Theory]
    [InlineData("(?i)b", "$1")]
    [InlineData("(?i:a)b", "$1")]
    [InlineData("(?i:a)(b)", "$2")]
    [InlineData("(?s)(?i)b", "$1")]
    public void DollarToken_BeyondTheCapturingGroups_NeverFires(string pattern, string replacement)
    {
        Assert.Empty(Scan($"SELECT REGEXP_REPLACE('abc', '{pattern}', '[{replacement}]');"));
    }

    [Theory]
    [InlineData("(?i:a)(b)", "$1")]
    [InlineData("(?P<x>b)", "$1")]
    [InlineData("(?i)(b)", "$1")]
    public void DollarToken_OnACapturingGroup_StillFires(string pattern, string replacement)
    {
        Assert.Single(Scan($"SELECT REGEXP_REPLACE('abc', '{pattern}', '[{replacement}]');"));
    }

    [Fact]
    public void BackslashBackreference_NeverFires()
    {
        var findings = Scan("SELECT REGEXP_REPLACE('abc123def', '([a-z]+)([0-9]+)', '\\2-\\1');");

        Assert.Empty(findings);
    }

    [Fact]
    public void DollarToken_NoCaptureGroupsInPattern_NeverFires()
    {
        var findings = Scan("SELECT REGEXP_REPLACE('price is 5', '[0-9]+', '$1 off');");

        Assert.Empty(findings);
    }

    [Fact]
    public void DollarToken_ExceedsCaptureGroupCount_NeverFires()
    {
        var findings = Scan("SELECT REGEXP_REPLACE('abc', '(a)', '$2');");

        Assert.Empty(findings);
    }

    [Fact]
    public void NonCapturingGroup_NotCountedAsCaptureGroup()
    {
        var findings = Scan("SELECT REGEXP_REPLACE('abc', '(?:a)(b)', '$2');");

        Assert.Empty(findings);
    }

    [Fact]
    public void NonLiteralReplacement_NeverFires()
    {
        var findings = Scan("DECLARE @r VARCHAR(10) = '$1'; SELECT REGEXP_REPLACE('abc', '(a)', @r);");

        Assert.Empty(findings);
    }

    [Fact]
    public void NonLiteralPattern_NeverFires()
    {
        var findings = Scan("DECLARE @p VARCHAR(10) = '(a)'; SELECT REGEXP_REPLACE('abc', @p, '$1');");

        Assert.Empty(findings);
    }

    [Fact]
    public void OtherFunction_NeverFires()
    {
        var findings = Scan("SELECT REGEXP_LIKE('abc', '(a)') UNION ALL SELECT CONCAT('$1', 'x');");

        Assert.Empty(findings);
    }

    [Theory]
    [InlineData(@"\Q(\E(b)", "$2", false)]
    [InlineData(@"\Q(\E(b)", "$1", true)]
    [InlineData(@"\Q(b)", "$1", false)]
    public void ParenthesesInsideAQuotedLiteralRun_AreNotGroups(string pattern, string replacement, bool fires)
    {
        Assert.Equal(fires, Scan($"SELECT REGEXP_REPLACE('a(b', '{pattern}', '{replacement}');").Count > 0);
    }
}
