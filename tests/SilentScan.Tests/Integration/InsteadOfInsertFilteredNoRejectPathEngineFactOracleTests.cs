using Microsoft.Data.SqlClient;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class InsteadOfInsertFilteredNoRejectPathEngineFactOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(InsteadOfInsertFilteredNoRejectPathEngineFactOracleTests);

    protected override string Ddl => """
        CREATE TABLE dbo.T1 (Id INT NOT NULL, Val INT NOT NULL);
        GO
        CREATE TRIGGER dbo.trg_T1_InsteadOfInsert ON dbo.T1 INSTEAD OF INSERT AS
        BEGIN
            INSERT INTO dbo.T1 (Id, Val)
            SELECT Id, Val FROM inserted WHERE Val > 0;
        END;
        GO
        """;

    [Fact]
    public async Task InsertOfThreeRows_WithOneFilteredOutByTheTrigger_CompletesWithNoErrorButWritesOnlyTwo()
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(DatabaseName));
        await connection.OpenAsync();

        await using var insertCommand = connection.CreateCommand();
        insertCommand.CommandText = "INSERT INTO dbo.T1 (Id, Val) VALUES (1, 5), (2, -1), (3, 10);";
        await insertCommand.ExecuteNonQueryAsync();

        await using var countCommand = connection.CreateCommand();
        countCommand.CommandText = "SELECT COUNT(*) FROM dbo.T1;";
        var actualRowCount = (int)(await countCommand.ExecuteScalarAsync())!;
        Assert.Equal(2, actualRowCount);
    }
}
