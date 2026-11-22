using SilentScan.Tests.Support;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.StringConcatNullRuleId)]
public sealed class StringConcatNullOuterJoinEngineFactOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(StringConcatNullOuterJoinEngineFactOracleTests);

    protected override string Ddl => """
        CREATE TABLE dbo.P (Id INT NOT NULL PRIMARY KEY, B VARCHAR(10) NOT NULL);
        CREATE TABLE dbo.Ch (Id INT NOT NULL PRIMARY KEY, C VARCHAR(10) NOT NULL);
        GO
        INSERT INTO dbo.P (Id, B) VALUES (1, 'a'), (2, 'b');
        INSERT INTO dbo.Ch (Id, C) VALUES (1, 'z');
        GO
        """;

    [Fact]
    public async Task NotNullColumnOnTheNullSuppliedSide_ConcatenatesToNullForUnmatchedRows_InnerJoinAndPreservedSideDoNot()
    {
        var leftJoinNulls = await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.P p LEFT JOIN dbo.Ch c ON c.Id = p.Id WHERE p.B + c.C IS NULL;");
        var innerJoinNulls = await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.P p INNER JOIN dbo.Ch c ON c.Id = p.Id WHERE p.B + c.C IS NULL;");
        var preservedSideNulls = await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.P p LEFT JOIN dbo.Ch c ON c.Id = p.Id WHERE p.B + p.B IS NULL;");

        Assert.Equal(1, leftJoinNulls);
        Assert.Equal(0, innerJoinNulls);
        Assert.Equal(0, preservedSideNulls);
    }
}
