using SilentScan.Tests.Support;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.TriggerCorrectnessInsteadOfInsertFilteredNoRejectPathRuleId)]
public sealed class InsteadOfInsertFilteredNoRejectPathEngineFactOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(InsteadOfInsertFilteredNoRejectPathEngineFactOracleTests);

    protected override string Ddl => """
        CREATE TABLE dbo.T1 (Id INT NOT NULL, Val INT NOT NULL);
        GO
        CREATE TABLE dbo.T1NotNullFilter (Id INT NOT NULL, Val INT NOT NULL);
        GO
        CREATE TRIGGER dbo.trg_T1NotNullFilter ON dbo.T1NotNullFilter INSTEAD OF INSERT AS
        BEGIN
            INSERT INTO dbo.T1NotNullFilter (Id, Val)
            SELECT Id, Val FROM inserted WHERE Val IS NOT NULL;
        END;
        GO
        CREATE TRIGGER dbo.trg_T1_InsteadOfInsert ON dbo.T1 INSTEAD OF INSERT AS
        BEGIN
            INSERT INTO dbo.T1 (Id, Val)
            SELECT Id, Val FROM inserted WHERE Val > 0;
        END;
        GO
        """;
    [Fact]
    public async Task InsertOfThreeRows_WithOneFilteredOutByTheTrigger_CompletesWithNoErrorButWritesOnlyTwo_UnfilteredControlWritesAllThree()
    {
        await ExecuteAsync("INSERT INTO dbo.T1 (Id, Val) VALUES (1, 5), (2, -1), (3, 10);");
        var filteredRowCount = await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.T1;");

        await ExecuteAsync("DELETE FROM dbo.T1; INSERT INTO dbo.T1 (Id, Val) VALUES (1, 5), (2, 7), (3, 10);");
        var controlRowCount = await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.T1;");

        Assert.Equal(2, filteredRowCount);
        Assert.Equal(3, controlRowCount);
    }

    [Fact]
    public async Task ReinsertFilteredOnlyByNotNullOnNotNullColumns_WritesEveryRow()
    {
        await ExecuteAsync("INSERT INTO dbo.T1NotNullFilter (Id, Val) VALUES (1, 5), (2, -1), (3, 10);");

        Assert.Equal(3, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.T1NotNullFilter;"));
    }
}
