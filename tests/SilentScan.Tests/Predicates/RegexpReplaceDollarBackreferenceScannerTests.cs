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
}
