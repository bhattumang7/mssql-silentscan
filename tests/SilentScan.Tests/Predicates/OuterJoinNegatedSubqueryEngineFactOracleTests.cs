using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.OuterJoinPredicateCollapseRuleId)]
public sealed class OuterJoinNegatedSubqueryEngineFactOracleTests : OracleTestFixture
{
    private const string From = "SELECT COUNT(*) FROM dbo.L AS l LEFT JOIN dbo.R AS r ON r.K = l.K WHERE ";

    protected override string DatabaseNameSeed => "SilentScanOuterJoinSubquery";

    protected override string Ddl => """
        CREATE TABLE dbo.L (K INT NOT NULL);
        CREATE TABLE dbo.R (K INT NOT NULL, V INT NOT NULL);
        CREATE TABLE dbo.Nothing (V INT NOT NULL);
        GO
        INSERT INTO dbo.L (K) VALUES (1), (2);
        INSERT INTO dbo.R (K, V) VALUES (1, 5);
        GO
        """;

    [Theory]
    [InlineData("r.V NOT IN (SELECT V FROM dbo.Nothing)", 2)]
    [InlineData("NOT (r.V IN (SELECT V FROM dbo.Nothing))", 2)]
    [InlineData("r.V IN (SELECT V FROM dbo.R)", 1)]
    [InlineData("r.V NOT IN (1, 2)", 1)]
    public async Task OnlyANegatedSubqueryTestCanKeepTheUnmatchedRow(string predicate, int expectedRows) =>
        Assert.Equal(expectedRows, await ScalarAsync<int>(From + predicate + ";"));
}
