using SilentScan.Tests.Support;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.NotInNullableSubqueryRuleId)]
public sealed class NotInNullableSubqueryOuterJoinEngineFactOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(NotInNullableSubqueryOuterJoinEngineFactOracleTests);

    protected override string Ddl => """
        CREATE TABLE dbo.Parent (Id INT NOT NULL PRIMARY KEY);
        CREATE TABLE dbo.A (Id INT NOT NULL PRIMARY KEY);
        CREATE TABLE dbo.B (Id INT NOT NULL PRIMARY KEY, RefId INT NOT NULL);
        GO
        INSERT INTO dbo.Parent (Id) VALUES (1), (2), (3);
        INSERT INTO dbo.A (Id) VALUES (10), (20);
        INSERT INTO dbo.B (Id, RefId) VALUES (10, 1);
        GO
        """;

    [Fact]
    public async Task NotNullColumnFromTheNullSuppliedSide_MakesNotInReturnNothing_InnerJoinControlReturnsTheRest()
    {
        var leftJoin = await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Parent WHERE Id NOT IN (SELECT b.RefId FROM dbo.A a LEFT JOIN dbo.B b ON b.Id = a.Id);");
        var innerJoin = await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Parent WHERE Id NOT IN (SELECT b.RefId FROM dbo.A a INNER JOIN dbo.B b ON b.Id = a.Id);");

        Assert.Equal(0, leftJoin);
        Assert.Equal(2, innerJoin);
    }
}
