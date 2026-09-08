using Microsoft.Data.SqlClient;
using SilentScan.Verify;
using SilentScan.Verify.Deployment;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class ViewOrderingOrderByNotGuaranteedToConsumerEngineFactOracleTests : IAsyncLifetime
{
    private static readonly SqlServerOptions Options = new(
        Host: "localhost",
        Port: int.TryParse(Environment.GetEnvironmentVariable("SILENTSCAN_SQL2025_PORT"), out var port) ? port : 14331,
        UserId: "sa",
        Password: Environment.GetEnvironmentVariable("SILENTSCAN_SA_PASSWORD") ?? "SilentScan!Dev2026");

    private readonly string _databaseName = $"{nameof(ViewOrderingOrderByNotGuaranteedToConsumerEngineFactOracleTests)}_{Guid.NewGuid():N}";

    private const string Ddl = """
        CREATE TABLE dbo.Orders (OrderId INT NOT NULL, Amount INT NOT NULL);
        INSERT INTO dbo.Orders (OrderId, Amount)
        VALUES (1,100),(2,100),(3,100),(4,100),(5,100),(6,100),(7,100),(8,100),(9,100),(10,100),(11,50);
        GO
        CREATE VIEW dbo.vTopOrders AS
            SELECT TOP (10) OrderId, Amount FROM dbo.Orders ORDER BY Amount DESC;
        GO
        """;

    public async Task InitializeAsync()
    {
        await new DatabaseProvisioner(Options).CreateFreshAsync(_databaseName);
        await new ScriptDeployer(Options).DeployAsync(Ddl, _databaseName);
    }

    public async Task DisposeAsync() =>
        await new DatabaseProvisioner(Options).DropIfExistsAsync(_databaseName);

    private static async Task<List<int>> ReadConsumerOrderAsync(SqlConnection connection)
    {
        await using var command = new SqlCommand("SELECT OrderId FROM dbo.vTopOrders;", connection);
        await using var reader = await command.ExecuteReaderAsync();

        var order = new List<int>();
        while (await reader.ReadAsync())
        {
            order.Add(reader.GetInt32(0));
        }

        return order;
    }

    [Fact]
    public async Task ConsumerRowOrder_ForIdenticalViewAndConsumerText_ChangesWithAnUnrelatedIndex()
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(_databaseName));
        await connection.OpenAsync();

        var beforeIndex = await ReadConsumerOrderAsync(connection);

        await using (var createIndex = new SqlCommand(
            "CREATE INDEX IX_Orders_AmountDesc ON dbo.Orders(Amount DESC) INCLUDE (OrderId);", connection))
        {
            await createIndex.ExecuteNonQueryAsync();
        }

        var afterIndex = await ReadConsumerOrderAsync(connection);

        Assert.NotEqual(beforeIndex, afterIndex);
        Assert.Equal([.. beforeIndex.OrderBy(id => id)], [.. afterIndex.OrderBy(id => id)]);
    }
}
