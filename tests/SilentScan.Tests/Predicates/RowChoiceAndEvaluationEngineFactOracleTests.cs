using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
public sealed class RowChoiceAndEvaluationEngineFactOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(RowChoiceAndEvaluationEngineFactOracleTests);

    protected override string Ddl => """
        CREATE TABLE dbo.T (Id INT NOT NULL PRIMARY KEY, Val INT NOT NULL);
        CREATE TABLE dbo.Cfg (Val INT NOT NULL);
        CREATE TABLE dbo.Seen (Val INT NOT NULL);
        INSERT INTO dbo.Cfg VALUES (7), (9);
        GO
        CREATE TRIGGER dbo.trg_T ON dbo.T AFTER INSERT AS
        BEGIN
            DECLARE @invariant INT, @perRow INT;
            SELECT @invariant = (SELECT MAX(Val) FROM dbo.Cfg) FROM inserted;
            SELECT @perRow = Val FROM inserted;
            INSERT INTO dbo.Seen SELECT @invariant FROM inserted;
            INSERT INTO dbo.Seen SELECT @perRow FROM inserted;
        END
        GO
        """;

    [Fact]
    [Trait("Rule", FindingRuleIds.TriggerCorrectnessMultiRowUnsafeSingleRowAssignmentRuleId)]
    public async Task RowInvariantAssignmentIsRightForEveryRow_PerRowAssignmentKeepsOneRowOnly()
    {
        await ExecuteAsync("INSERT INTO dbo.T VALUES (1, 100), (2, 200), (3, 300);");

        Assert.Equal(3, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Seen WHERE Val = 9;"));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(DISTINCT Val) FROM dbo.Seen WHERE Val >= 100;"));
    }

    [Fact]
    [Trait("Rule", FindingRuleIds.BareTopNoOrderByRuleId)]
    public async Task TopOverAOneRowSourceHasNoRowToChoose()
    {
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM (SELECT TOP (5) v.x FROM (VALUES (1)) AS v(x)) AS q;"));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM (SELECT TOP (1) v.x FROM (VALUES (1), (2)) AS v(x)) AS q;"));
    }

    [Fact]
    [Trait("Rule", FindingRuleIds.ControlFlowRiskNonDeterministicCaseInputRuleId)]
    public async Task ASingleWhenEvaluatesTheCaseInputOnce()
    {
        var misses = await ScalarAsync<int>("""
            SELECT COUNT(*) FROM
            (SELECT CASE ABS(CHECKSUM(NEWID())) % 2 WHEN 0 THEN 'a' ELSE 'b' END AS c
             FROM (SELECT TOP (3000) a.object_id FROM sys.all_columns AS a CROSS JOIN sys.all_columns AS b) AS n) AS q
            WHERE q.c IS NULL;
            """);

        Assert.Equal(0, misses);
    }

    [Theory]
    [Trait("Rule", FindingRuleIds.ViewOrderingTopPercentOrderByNeverLimitsRuleId)]
    [InlineData("100", false)]
    [InlineData("1E2", false)]
    [InlineData("100.0000000000000001", false)]
    [InlineData("50", true)]
    public async Task TopPercentThatConvertsToOneHundredNeedsNoSort(string percent, bool sorts)
    {
        await using var connection = await OpenConnectionAsync();
        await ExecuteAsync(connection, "CREATE OR ALTER VIEW dbo.v AS SELECT TOP (" + percent + ") PERCENT Id, Val FROM dbo.T ORDER BY Val;");
        await ExecuteAsync(connection, "SET SHOWPLAN_XML ON;");
        var plan = await ScalarAsync<string>(connection, "SELECT Id FROM dbo.v;");

        Assert.Equal(sorts, plan!.Contains("PhysicalOp=\"Sort\"", StringComparison.Ordinal));
    }
}
