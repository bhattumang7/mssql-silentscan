using Microsoft.Data.SqlClient;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class TriggerRecursionCycleEngineFactOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(TriggerRecursionCycleEngineFactOracleTests);

    protected override string Ddl => """
        EXEC sp_configure 'nested triggers', 1;
        RECONFIGURE;
        GO
        CREATE TABLE dbo.A (Id INT NOT NULL);
        CREATE TABLE dbo.B (Id INT NOT NULL);
        GO
        CREATE TRIGGER dbo.trg_A_Cycle ON dbo.A AFTER INSERT AS
        BEGIN
            INSERT INTO dbo.B (Id) SELECT Id FROM inserted;
        END;
        GO
        CREATE TRIGGER dbo.trg_B_Cycle ON dbo.B AFTER INSERT AS
        BEGIN
            INSERT INTO dbo.A (Id) SELECT Id FROM inserted;
        END;
        GO
        """;

    [Fact]
    public async Task TwoTableTriggerCycle_WithNestedTriggersOn_RaisesMsg217()
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(DatabaseName));
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO dbo.A (Id) VALUES (1);";

        var exception = await Assert.ThrowsAsync<SqlException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal(217, exception.Number);
    }
}
