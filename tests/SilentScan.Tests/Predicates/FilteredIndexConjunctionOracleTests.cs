using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.FilteredIndexParameterMismatchRuleId)]
public sealed class FilteredIndexConjunctionOracleTests : OracleTestFixture
{
    private const int NoPlanBecauseOfHints = 8622;

    protected override string DatabaseNameSeed => nameof(FilteredIndexConjunctionOracleTests);

    protected override string Ddl => """
        CREATE TABLE dbo.Orders (Id INT NOT NULL PRIMARY KEY, Status INT NOT NULL, CustomerId INT NOT NULL);
        GO
        CREATE INDEX IX_Open ON dbo.Orders (CustomerId) INCLUDE (Status) WHERE Status = 1;
        GO
        CREATE PROCEDURE dbo.Pinned @s INT AS SELECT CustomerId FROM dbo.Orders WITH (INDEX(IX_Open)) WHERE Status = @s AND Status = 1;
        GO
        CREATE PROCEDURE dbo.Unpinned @s INT AS SELECT CustomerId FROM dbo.Orders WITH (INDEX(IX_Open)) WHERE Status = @s;
        GO
        """;

    [Fact]
    public async Task ALiteralConjunctMatchingTheFilterMakesTheFilteredIndexUsableBesideAParameter()
    {
        Assert.Null(await SqlErrorNumberAsync("EXEC dbo.Pinned @s = 1;"));
        Assert.Equal(NoPlanBecauseOfHints, await SqlErrorNumberAsync("EXEC dbo.Unpinned @s = 1;"));
    }
}
