using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;

namespace SilentScan.Tests.Predicates;

public sealed class JsonArrayAggMissingOrderScannerTests
{
    private static IReadOnlyList<JsonArrayAggMissingOrderFinding> Scan(string sql)
    {
        var result = SqlScriptParser.ParseText("test.sql", sql);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        return JsonArrayAggMissingOrderScanner.Scan(result);
    }

    [Fact]
    public void JsonArrayAgg_NoOrder_Fires()
    {
        var findings = Scan("SELECT JSON_ARRAYAGG(Name) FROM dbo.T GROUP BY GroupId;");

        Assert.Single(findings);
    }

    [Fact]
    public void JsonArrayAgg_WithOwnOrderBy_NeverFires()
    {
        var findings = Scan("SELECT JSON_ARRAYAGG(Name ORDER BY Name) FROM dbo.T GROUP BY GroupId;");

        Assert.Empty(findings);
    }

    [Fact]
    public void OtherAggregate_NeverFires()
    {
        var findings = Scan("SELECT STRING_AGG(Name, ',') WITHIN GROUP (ORDER BY Name) FROM dbo.T GROUP BY GroupId;");

        Assert.Empty(findings);
    }

    [Fact]
    public void JsonArrayAgg_InsideStoredProcedure_Fires()
    {
        var findings = Scan(
            "CREATE PROCEDURE dbo.usp_Find AS BEGIN SELECT JSON_ARRAYAGG(Name) FROM dbo.T GROUP BY GroupId; END");

        Assert.Single(findings);
    }
}
