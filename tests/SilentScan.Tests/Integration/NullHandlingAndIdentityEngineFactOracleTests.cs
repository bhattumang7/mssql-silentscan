using SilentScan.Tests.Support;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class NullHandlingAndIdentityEngineFactOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(NullHandlingAndIdentityEngineFactOracleTests);

    protected override string Ddl => """
        CREATE TABLE dbo.A (Id INT NOT NULL PRIMARY KEY);
        CREATE TABLE dbo.B (AId INT NOT NULL, Y INT NULL, Name VARCHAR(20) NULL);
        CREATE TABLE dbo.P (Id INT NOT NULL PRIMARY KEY, Name VARCHAR(20) NULL);
        INSERT INTO dbo.A VALUES (1);
        INSERT INTO dbo.B VALUES (2, 5, 'x');
        INSERT INTO dbo.P VALUES (1, 'a'), (2, NULL);
        GO
        CREATE TABLE dbo.Plain (Id INT IDENTITY(100, 1) PRIMARY KEY, V INT);
        CREATE TABLE dbo.Side (Id INT IDENTITY(500, 1) PRIMARY KEY, V INT);
        CREATE TABLE dbo.Audited (Id INT IDENTITY(900, 1) PRIMARY KEY, V INT);
        GO
        CREATE TRIGGER dbo.AuditedInsert ON dbo.Audited AFTER INSERT AS INSERT INTO dbo.Side (V) SELECT V FROM inserted;
        """;

    private const string Collapse = "SELECT COUNT(*) FROM dbo.A AS a LEFT JOIN dbo.B AS b ON b.AId = a.Id WHERE ";

    [Theory]
    [Trait("Rule", FindingRuleIds.OuterJoinPredicateCollapseRuleId)]
    [InlineData("SET CONCAT_NULL_YIELDS_NULL OFF;", "b.Name + 'y' = 'y'", 1)]
    [InlineData("SET CONCAT_NULL_YIELDS_NULL ON;", "b.Name + 'y' = 'y'", 0)]
    [InlineData("SET ANSI_NULLS OFF;", "b.Y = NULL", 1)]
    [InlineData("SET ANSI_NULLS OFF;", "b.Y NOT IN (1, 2)", 1)]
    [InlineData("SET ANSI_NULLS OFF;", "b.Y <> 1", 0)]
    public async Task SessionSettingsDecideWhetherThePredicateKeepsTheNullExtendedRow(string setting, string predicate, int rows)
    {
        await using var connection = await OpenConnectionAsync();
        await ExecuteAsync(connection, setting);

        Assert.Equal(rows, await ScalarAsync<int>(connection, Collapse + predicate + ";"));
    }

    [Theory]
    [Trait("Rule", FindingRuleIds.StringConcatNullRuleId)]
    [InlineData("WHERE p.Name IS NOT NULL", 0)]
    [InlineData("", 1)]
    public async Task AnOuterNullFilterKeepsACorrelatedConcatenationFromYieldingNull(string where, int nulls)
    {
        var result = await ScalarAsync<int>(
            $"SELECT COUNT(*) - COUNT(x.v) FROM dbo.P AS p CROSS APPLY (SELECT p.Name + 'x' AS v) AS x {where};");

        Assert.Equal(nulls, result);
    }

    [Theory]
    [Trait("Rule", FindingRuleIds.ControlFlowRiskLegacyIdentityIntrinsicRuleId)]
    [InlineData("Plain", true)]
    [InlineData("Audited", false)]
    public async Task IdentitySlotsAgreeAfterAnInsertWithNoTrigger(string table, bool agree)
    {
        var result = await ScalarAsync<int>(
            $"INSERT INTO dbo.{table} (V) VALUES (1); SELECT CASE WHEN @@IDENTITY = SCOPE_IDENTITY() THEN 1 ELSE 0 END;");

        Assert.Equal(agree ? 1 : 0, result);
    }

    [Theory]
    [Trait("Rule", FindingRuleIds.CartesianJoinCommaJoinRuleId)]
    [InlineData("a.Id = 1 AND a.Id = 2", false)]
    [InlineData("a.Id = 1 AND b.AId = 2", true)]
    public async Task AContradictoryWhereClauseCompilesWithoutReadingTheJoinedTables(string where, bool readsTables)
    {
        var plan = await PlanInSessionAsync(string.Empty, $"SELECT a.Id FROM dbo.A AS a, dbo.B AS b WHERE {where};");

        Assert.Equal(readsTables, ShowPlan.PhysicalOps(plan).Any(op => op.Contains("Scan", StringComparison.Ordinal) && op != "Constant Scan"));
    }

    [Theory]
    [Trait("Rule", FindingRuleIds.ViewOrderingOrderByNotGuaranteedToConsumerRuleId)]
    [InlineData("(SELECT NULL)", false)]
    [InlineData("Name", true)]
    public async Task ARowIndependentOrderKeyProducesNoSortForTop(string key, bool sorts)
    {
        var plan = await PlanInSessionAsync(string.Empty, $"SELECT TOP (1) Id FROM dbo.P ORDER BY {key};");

        Assert.Equal(sorts, ShowPlan.PhysicalOps(plan).Contains("Sort"));
    }
}
