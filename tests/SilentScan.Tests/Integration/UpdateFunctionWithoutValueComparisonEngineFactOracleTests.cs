using SilentScan.Tests.Support;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
[Trait("Rule", "silentscan/trigger/update-function-without-value-comparison")]
public sealed class UpdateFunctionWithoutValueComparisonEngineFactOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(UpdateFunctionWithoutValueComparisonEngineFactOracleTests);

    protected override string Ddl => """
        CREATE TABLE dbo.T2 (Id INT NOT NULL, Val INT NOT NULL);
        INSERT INTO dbo.T2 (Id, Val) VALUES (1, 100);
        GO
        CREATE TABLE dbo.T2Log (Msg NVARCHAR(100) NOT NULL);
        GO
        CREATE TRIGGER dbo.trg_T2_Update ON dbo.T2 AFTER UPDATE AS
        BEGIN
            IF UPDATE(Val)
                INSERT INTO dbo.T2Log (Msg) VALUES ('Val touched');
        END;
        GO
        """;

    [Fact]
    public async Task UpdateFunction_ReturnsTrue_ForAColumnRewrittenWithItsOwnUnchangedValue_ColumnAbsentFromSetListControlDoesNot()
    {
        await ExecuteAsync("UPDATE dbo.T2 SET Id = 1 WHERE Id = 1;");
        var loggedWithColumnAbsentFromSetList = await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.T2Log;");

        await ExecuteAsync("UPDATE dbo.T2 SET Val = 100 WHERE Id = 1;");
        var loggedWithUnchangedValueRewritten = await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.T2Log;");

        Assert.Equal(0, loggedWithColumnAbsentFromSetList);
        Assert.Equal(1, loggedWithUnchangedValueRewritten);
    }
}
