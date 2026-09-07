using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;

namespace SilentScan.Tests.Predicates;

public sealed class StringAggMissingOrderScannerTests
{
    private static IReadOnlyList<StringAggMissingOrderFinding> Scan(string sql)
    {
        var result = SqlScriptParser.ParseText("test.sql", sql);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        return StringAggMissingOrderScanner.Scan(result);
    }

    [Fact]
    public void StringAgg_NoWithinGroup_Fires()
    {
        var findings = Scan("SELECT STRING_AGG(Name, ',') FROM dbo.T GROUP BY GroupId;");

        Assert.Single(findings);
    }

    [Fact]
    public void StringAgg_WithWithinGroupOrderBy_NeverFires()
    {
        var findings = Scan("SELECT STRING_AGG(Name, ',') WITHIN GROUP (ORDER BY Name) FROM dbo.T GROUP BY GroupId;");

        Assert.Empty(findings);
    }

    [Fact]
    public void OtherAggregate_NeverFires()
    {
        var findings = Scan("SELECT SUM(Amount) FROM dbo.T GROUP BY GroupId;");

        Assert.Empty(findings);
    }

    [Fact]
    public void StringAgg_InsideStoredProcedure_Fires()
    {
        var findings = Scan(
            "CREATE PROCEDURE dbo.usp_Find AS BEGIN SELECT STRING_AGG(Name, ',') FROM dbo.T GROUP BY GroupId; END");

        Assert.Single(findings);
    }

    [Fact]
    public void TwoIndependentStringAggCalls_NoWithinGroup_BothFire()
    {
        var findings = Scan(
            "SELECT STRING_AGG(Name, ',') FROM dbo.T GROUP BY GroupId; SELECT STRING_AGG(City, ';') FROM dbo.U GROUP BY RegionId;");

        Assert.Equal(2, findings.Count);
    }
}
