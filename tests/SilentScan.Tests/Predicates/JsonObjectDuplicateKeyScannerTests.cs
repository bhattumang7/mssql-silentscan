using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;

namespace SilentScan.Tests.Predicates;

public sealed class JsonObjectDuplicateKeyScannerTests
{
    private static IReadOnlyList<JsonObjectDuplicateKeyFinding> Scan(string sql)
    {
        var result = SqlScriptParser.ParseText("test.sql", sql);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        return JsonObjectDuplicateKeyScanner.Scan(result);
    }

    [Fact]
    public void JsonObject_DuplicateLiteralKey_Fires()
    {
        var findings = Scan("SELECT JSON_OBJECT('a':1, 'a':2);");

        var finding = Assert.Single(findings);
        Assert.Equal("a", finding.DuplicateKey);
    }

    [Fact]
    public void JsonObject_DistinctKeys_NeverFires()
    {
        var findings = Scan("SELECT JSON_OBJECT('a':1, 'b':2);");

        Assert.Empty(findings);
    }

    [Fact]
    public void JsonObject_DuplicateKeyDifferentCase_NeverFires()
    {
        var findings = Scan("SELECT JSON_OBJECT('a':1, 'A':2);");

        Assert.Empty(findings);
    }

    [Fact]
    public void JsonObject_NonLiteralKey_NeverFires()
    {
        var findings = Scan("DECLARE @k VARCHAR(10) = 'a'; SELECT JSON_OBJECT(@k:1, 'a':2);");

        Assert.Empty(findings);
    }

    [Fact]
    public void JsonObject_ThreeKeysTwoDuplicate_FiresOnce()
    {
        var findings = Scan("SELECT JSON_OBJECT('a':1, 'b':2, 'a':3);");

        var finding = Assert.Single(findings);
        Assert.Equal("a", finding.DuplicateKey);
    }

    [Fact]
    public void OtherFunction_NeverFires()
    {
        var findings = Scan("SELECT JSON_OBJECT('a':1, 'b':2) UNION ALL SELECT CONCAT('a', 'a');");

        Assert.Empty(findings);
    }
}
