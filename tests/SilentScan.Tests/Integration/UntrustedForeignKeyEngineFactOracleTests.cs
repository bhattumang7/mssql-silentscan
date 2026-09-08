using Microsoft.Data.SqlClient;
using SilentScan.Verify;
using SilentScan.Verify.Deployment;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class UntrustedForeignKeyEngineFactOracleTests : IAsyncLifetime
{
    private static readonly SqlServerOptions Options = new(
        Host: "localhost",
        Port: int.TryParse(Environment.GetEnvironmentVariable("SILENTSCAN_SQL2025_PORT"), out var port) ? port : 14331,
        UserId: "sa",
        Password: Environment.GetEnvironmentVariable("SILENTSCAN_SA_PASSWORD") ?? "SilentScan!Dev2026");

    private readonly string _databaseName = $"{nameof(UntrustedForeignKeyEngineFactOracleTests)}_{Guid.NewGuid():N}";

    private const string Ddl = """
        CREATE TABLE dbo.Customers (CustomerId INT NOT NULL PRIMARY KEY, Name VARCHAR(50) NOT NULL);
        CREATE TABLE dbo.Orders (OrderId INT NOT NULL PRIMARY KEY, CustomerId INT NOT NULL);
        INSERT INTO dbo.Customers (CustomerId, Name) VALUES (1, 'A');
        INSERT INTO dbo.Orders (OrderId, CustomerId) VALUES (1, 1);
        ALTER TABLE dbo.Orders WITH NOCHECK ADD CONSTRAINT FK_Orders_Customers
            FOREIGN KEY (CustomerId) REFERENCES dbo.Customers (CustomerId);
        GO
        """;

    private const string Query = """
        SELECT O.OrderId FROM dbo.Orders O JOIN dbo.Customers C ON O.CustomerId = C.CustomerId;
        """;

    public async Task InitializeAsync()
    {
        await new DatabaseProvisioner(Options).CreateFreshAsync(_databaseName);
        await new ScriptDeployer(Options).DeployAsync(Ddl, _databaseName);
    }

    public async Task DisposeAsync() =>
        await new DatabaseProvisioner(Options).DropIfExistsAsync(_databaseName);

    private static async Task<string> CaptureJoinPlanAsync(SqlConnection connection)
    {
        await using (var onCommand = new SqlCommand("SET STATISTICS XML ON;", connection))
        {
            await onCommand.ExecuteNonQueryAsync();
        }

        var planXmlBuilder = new System.Text.StringBuilder();
        await using (var probeCommand = new SqlCommand(Query, connection))
        await using (var reader = await probeCommand.ExecuteReaderAsync())
        {
            do
            {
                while (await reader.ReadAsync())
                {
                    if (reader.FieldCount == 1 && reader.GetFieldType(0) == typeof(string))
                    {
                        var value = reader.GetString(0);
                        if (value.Contains("ShowPlanXML", StringComparison.Ordinal))
                        {
                            planXmlBuilder.Append(value);
                        }
                    }
                }
            }
            while (await reader.NextResultAsync());
        }

        await using (var offCommand = new SqlCommand("SET STATISTICS XML OFF;", connection))
        {
            await offCommand.ExecuteNonQueryAsync();
        }

        var planXml = planXmlBuilder.ToString();
        Assert.NotEmpty(planXml);
        return planXml;
    }

    [Fact]
    public async Task UntrustedForeignKey_KeepsTheReferencedTableInThePlanEvenThoughOnlyTheReferencingColumnsAreSelected()
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(_databaseName));
        await connection.OpenAsync();

        var planXml = await CaptureJoinPlanAsync(connection);

        Assert.Contains("Table=\"[Customers]\"", planXml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TrustingTheForeignKey_EliminatesTheReferencedTableFromThePlanEntirely()
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(_databaseName));
        await connection.OpenAsync();

        await using (var trustCommand = new SqlCommand(
            "ALTER TABLE dbo.Orders WITH CHECK CHECK CONSTRAINT FK_Orders_Customers;", connection))
        {
            await trustCommand.ExecuteNonQueryAsync();
        }

        var planXml = await CaptureJoinPlanAsync(connection);

        Assert.DoesNotContain("Table=\"[Customers]\"", planXml, StringComparison.Ordinal);
        Assert.Contains("Table=\"[Orders]\"", planXml, StringComparison.Ordinal);
    }
}
