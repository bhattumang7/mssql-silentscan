using Microsoft.Data.SqlClient;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class TriggerOrderEngineFactOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(TriggerOrderEngineFactOracleTests);

    protected override string Ddl => """
        CREATE TABLE dbo.Orders (OrderId INT NOT NULL, Status VARCHAR(20) NULL);
        CREATE TABLE dbo.OrderAudit (OrderId INT NOT NULL);
        GO
        CREATE TRIGGER dbo.trg_Orders_Audit ON dbo.Orders AFTER INSERT AS
            INSERT INTO dbo.OrderAudit (OrderId) SELECT OrderId FROM inserted;
        GO
        CREATE TRIGGER dbo.trg_Orders_Validate ON dbo.Orders AFTER INSERT AS
            UPDATE dbo.Orders SET Status = 'Validated' WHERE OrderId IN (SELECT OrderId FROM inserted);
        GO
        """;

    private const string ReadTriggerOrderQuery = """
        SELECT OBJECT_NAME(te.object_id), te.is_first, te.is_last
        FROM sys.trigger_events te
        JOIN sys.triggers t ON t.object_id = te.object_id
        WHERE t.parent_id = OBJECT_ID('dbo.Orders')
        ORDER BY OBJECT_NAME(te.object_id);
        """;

    [Fact]
    public async Task TwoUnpinnedTriggersOnTheSameEvent_BothReportNeitherFirstNorLastUntilPinned()
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(DatabaseName));
        await connection.OpenAsync();

        await using (var beforeCommand = new SqlCommand(ReadTriggerOrderQuery, connection))
        await using (var reader = await beforeCommand.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                Assert.False(reader.GetBoolean(1));
                Assert.False(reader.GetBoolean(2));
            }
        }

        await using (var pinCommand = new SqlCommand(
            "EXEC sp_settriggerorder @triggername = 'dbo.trg_Orders_Validate', @order = 'First', @stmttype = 'INSERT';",
            connection))
        {
            await pinCommand.ExecuteNonQueryAsync();
        }

        var pinnedResults = new Dictionary<string, (bool IsFirst, bool IsLast)>();
        await using (var afterCommand = new SqlCommand(ReadTriggerOrderQuery, connection))
        await using (var reader = await afterCommand.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                pinnedResults[reader.GetString(0)] = (reader.GetBoolean(1), reader.GetBoolean(2));
            }
        }

        Assert.Equal((true, false), pinnedResults["trg_Orders_Validate"]);
        Assert.Equal((false, false), pinnedResults["trg_Orders_Audit"]);
    }
}
