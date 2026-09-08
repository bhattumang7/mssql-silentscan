using Microsoft.Data.SqlClient;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
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
    public async Task UpdateFunction_ReturnsTrue_ForAColumnRewrittenWithItsOwnUnchangedValue()
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(DatabaseName));
        await connection.OpenAsync();

        await using var updateCommand = connection.CreateCommand();
        updateCommand.CommandText = "UPDATE dbo.T2 SET Val = 100 WHERE Id = 1;";
        await updateCommand.ExecuteNonQueryAsync();

        await using var countCommand = connection.CreateCommand();
        countCommand.CommandText = "SELECT COUNT(*) FROM dbo.T2Log;";
        var triggerFiredCount = (int)(await countCommand.ExecuteScalarAsync())!;
        Assert.Equal(1, triggerFiredCount);
    }
}
