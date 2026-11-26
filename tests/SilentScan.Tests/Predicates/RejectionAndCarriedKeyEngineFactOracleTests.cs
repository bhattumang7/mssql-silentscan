using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.TriggerCorrectnessMultiRowUnsafeSingleRowAssignmentRuleId)]
[Trait("Rule", FindingRuleIds.TriggerCorrectnessMultiRowUnsafeKeyedDmlRuleId)]
[Trait("Rule", FindingRuleIds.TriggerCorrectnessInsteadOfInsertFilteredNoRejectPathRuleId)]
[Trait("Rule", FindingRuleIds.IndexDesignFilterColumnNotInIndexRuleId)]
public sealed class RejectionAndCarriedKeyEngineFactOracleTests : OracleTestFixture
{
    private const int SubqueryReturnedMoreThanOneValue = 512;

    private const int TransactionEndedInTrigger = 3609;

    protected override string DatabaseNameSeed => "SilentScanRejectionFacts";

    protected override string Ddl => """
        CREATE TABLE dbo.Scalar (Val INT NOT NULL);
        CREATE TABLE dbo.Rejecting (Val INT NOT NULL);
        CREATE TABLE dbo.Carried (Region INT NOT NULL, Id INT NOT NULL, Amount INT NOT NULL, CONSTRAINT PK_Carried PRIMARY KEY CLUSTERED (Region, Id));
        CREATE TABLE dbo.Uncarried (Id INT NOT NULL PRIMARY KEY, Region INT NOT NULL, Amount INT NOT NULL);
        GO
        CREATE TRIGGER dbo.ScalarTrigger ON dbo.Scalar AFTER INSERT AS BEGIN DECLARE @v INT; SET @v = (SELECT Val FROM inserted); END;
        GO
        CREATE TRIGGER dbo.RejectingTrigger ON dbo.Rejecting INSTEAD OF INSERT AS
        BEGIN
            IF EXISTS (SELECT 1 FROM inserted WHERE Val <= 0) BEGIN ROLLBACK TRANSACTION; RETURN; END;
            INSERT INTO dbo.Rejecting (Val) SELECT Val FROM inserted WHERE Val > 0;
        END;
        GO
        CREATE INDEX IX_Carried ON dbo.Carried (Amount) WHERE Region = 7;
        CREATE INDEX IX_Uncarried ON dbo.Uncarried (Amount) WHERE Region = 7;
        GO
        """;

    [Fact]
    public async Task SettingAVariableFromAMultiRowScalarSubquery_RaisesInsteadOfPickingARow() =>
        Assert.Equal(SubqueryReturnedMoreThanOneValue, await SqlErrorNumberAsync("INSERT INTO dbo.Scalar (Val) VALUES (1), (2);"));

    [Fact]
    public async Task RollingBackInAnInsteadOfTrigger_TellsTheCallerTheRowsWereRejected()
    {
        Assert.Equal(TransactionEndedInTrigger, await SqlErrorNumberAsync("INSERT INTO dbo.Rejecting (Val) VALUES (1), (-1);"));
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Rejecting;"));
    }

    [Theory]
    [InlineData("Carried", false)]
    [InlineData("Uncarried", true)]
    public async Task AFilterColumnNeedsALookupOnlyWhenTheClusteringKeyDoesNotCarryIt(string table, bool looksUp)
    {
        await ExecuteAsync($"UPDATE STATISTICS dbo.{table};");

        var plan = await PlanInSessionAsync(string.Empty, $"SELECT Amount, Region FROM dbo.{table} WITH (INDEX(IX_{table})) WHERE Region = 7 AND Amount = 5;");

        Assert.Equal(looksUp, plan.Descendants().Any(e => (string?)e.Attribute("Lookup") is "1" or "true"));
    }
}
