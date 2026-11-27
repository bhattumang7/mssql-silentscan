using SilentScan.Tests.Support;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class PinningAndPaddingEngineFactOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(PinningAndPaddingEngineFactOracleTests);

    protected override string Ddl => """
        SET ANSI_PADDING OFF;
        CREATE TABLE dbo.Padding (CharNull CHAR(5) NULL, CharNotNull CHAR(5) NOT NULL, Bytes VARBINARY(5) NULL);
        GO
        SET ANSI_PADDING ON;
        INSERT INTO dbo.Padding VALUES ('a  ', 'a  ', 0x612020);
        GO
        CREATE TABLE dbo.U (Id INT NOT NULL PRIMARY KEY, R INT NOT NULL);
        CREATE TABLE dbo.U2 (Id INT NOT NULL PRIMARY KEY, R INT NOT NULL);
        INSERT INTO dbo.U VALUES (1, 10), (2, 10), (3, 10);
        INSERT INTO dbo.U2 VALUES (10, 1);
        GO
        CREATE TABLE dbo.Target (Id INT NOT NULL PRIMARY KEY, V INT NOT NULL);
        CREATE TABLE dbo.Hop (Id INT NOT NULL PRIMARY KEY, X INT NOT NULL);
        CREATE TABLE dbo.Leaf (X INT NOT NULL, V INT NOT NULL, W INT NOT NULL);
        INSERT INTO dbo.Target VALUES (1, 0);
        INSERT INTO dbo.Hop VALUES (1, 7);
        INSERT INTO dbo.Leaf VALUES (7, 10, 2), (7, 20, 1);
        """;

    [Theory]
    [Trait("Rule", FindingRuleIds.AnsiPaddingMismatchRuleId)]
    [InlineData("CharNull LIKE 'a  '", 0)]
    [InlineData("CharNotNull LIKE 'a  '", 1)]
    [InlineData("Bytes LIKE 'a '", 1)]
    public async Task PaddingOffTrimsNullableCharButNeitherNotNullCharNorTrailingSpaceBytes(string predicate, int rows)
    {
        Assert.Equal(rows, await ScalarAsync<int>($"SELECT COUNT(*) FROM dbo.Padding WHERE {predicate};"));
    }

    [Theory]
    [Trait("Rule", FindingRuleIds.BareTopNoOrderByRuleId)]
    [InlineData("dbo.U AS a JOIN dbo.U2 AS b ON b.Id = a.R WHERE a.Id = 1", 1)]
    [InlineData("dbo.U AS a LEFT JOIN dbo.U2 AS b ON a.Id = 1 AND b.Id = 10", 3)]
    public async Task OnlyAWherePinOrANullSupplyingJoinPinLimitsTheRows(string from, int rows)
    {
        Assert.Equal(rows, await ScalarAsync<int>($"SELECT COUNT(*) FROM {from};"));
    }

    [Fact]
    [Trait("Rule", FindingRuleIds.NonUniqueUpdateSourceRuleId)]
    public async Task ASecondHopNonUniqueSourceWritesWhicheverRowThePlanReadsFirst()
    {
        const string update =
            "UPDATE t SET V = s.V FROM dbo.Target AS t JOIN dbo.Hop AS h ON h.Id = t.Id JOIN dbo.Leaf AS s ON s.X = h.X;";

        await ExecuteAsync("CREATE CLUSTERED INDEX CX ON dbo.Leaf (X, V);");
        await ExecuteAsync(update);
        var byValue = await ScalarAsync<int>("SELECT V FROM dbo.Target;");

        await ExecuteAsync("CREATE CLUSTERED INDEX CX ON dbo.Leaf (X, W) WITH (DROP_EXISTING = ON);");
        await ExecuteAsync(update);
        var byOtherColumn = await ScalarAsync<int>("SELECT V FROM dbo.Target;");

        Assert.NotEqual(byValue, byOtherColumn);
    }
}
