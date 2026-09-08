using Microsoft.Data.SqlClient;
using SilentScan.Verify;
using SilentScan.Verify.Deployment;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class TableVariablePspSkipEngineFactOracleTests : IAsyncLifetime
{
    private static readonly SqlServerOptions Options = new(
        Host: "localhost",
        Port: int.TryParse(Environment.GetEnvironmentVariable("SILENTSCAN_SQL2025_PORT"), out var port) ? port : 14331,
        UserId: "sa",
        Password: Environment.GetEnvironmentVariable("SILENTSCAN_SA_PASSWORD") ?? "SilentScan!Dev2026");

    private readonly string _databaseName = $"{nameof(TableVariablePspSkipEngineFactOracleTests)}_{Guid.NewGuid():N}";

    private const string Ddl = """
        ALTER DATABASE CURRENT SET COMPATIBILITY_LEVEL = 170;
        ALTER DATABASE CURRENT SET QUERY_STORE = ON;
        ALTER DATABASE CURRENT SET QUERY_STORE (OPERATION_MODE = READ_WRITE, QUERY_CAPTURE_MODE = ALL);
        GO
        CREATE TABLE dbo.Orders (Id INT NOT NULL PRIMARY KEY, CustomerId INT NOT NULL);
        CREATE INDEX IX_Orders_CustomerId ON dbo.Orders(CustomerId);
        GO
        INSERT INTO dbo.Orders (Id, CustomerId)
        SELECT TOP (200000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)), 1
        FROM sys.all_objects a CROSS JOIN sys.all_objects b;
        INSERT INTO dbo.Orders (Id, CustomerId) VALUES (200001, 2), (200002, 3), (200003, 4), (200004, 5);
        GO
        UPDATE STATISTICS dbo.Orders WITH FULLSCAN;
        GO
        CREATE TYPE dbo.IdList AS TABLE (Id INT NOT NULL PRIMARY KEY);
        GO
        CREATE PROCEDURE dbo.StatementReadsTvp @CustomerId INT, @Ids dbo.IdList READONLY
        AS
        SELECT COUNT(*) FROM dbo.Orders o WHERE o.CustomerId = @CustomerId AND EXISTS (SELECT 1 FROM @Ids i WHERE i.Id = o.Id);
        GO
        CREATE PROCEDURE dbo.StatementIgnoresTvp @CustomerId INT, @Ids dbo.IdList READONLY
        AS
        SELECT COUNT(*) FROM dbo.Orders WHERE CustomerId = @CustomerId;
        GO
        """;

    public async Task InitializeAsync()
    {
        await new DatabaseProvisioner(Options).CreateFreshAsync(_databaseName);
        await new ScriptDeployer(Options).DeployAsync(Ddl, _databaseName);
    }

    public async Task DisposeAsync() =>
        await new DatabaseProvisioner(Options).DropIfExistsAsync(_databaseName);

    private static async Task ExecuteAsync(SqlConnection connection, string procedureName, int customerId)
    {
        var batch = $"""
            DECLARE @ids dbo.IdList;
            EXEC dbo.{procedureName} @CustomerId = {customerId}, @Ids = @ids;
            """;
        await using var command = new SqlCommand(batch, connection);
        await command.ExecuteScalarAsync();
    }

    private static async Task<List<string>> CapturePlanTypesAsync(SqlConnection connection, string procedureName)
    {
        const string query = """
            SELECT qsp.plan_type_desc
            FROM sys.query_store_query qsq
            JOIN sys.query_store_plan qsp ON qsp.query_id = qsq.query_id
            JOIN sys.objects o ON o.object_id = qsq.object_id
            WHERE o.name = @procedureName
            ORDER BY qsp.plan_id;
            """;

        await using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@procedureName", procedureName);

        var planTypes = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            planTypes.Add(reader.GetString(0));
        }

        return planTypes;
    }

    [Fact]
    public async Task StatementReadingTheTableValuedParameter_NeverGetsAParameterSensitivePlanDispatcher()
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(_databaseName));
        await connection.OpenAsync();

        await ExecuteAsync(connection, "StatementReadsTvp", customerId: 1);
        await ExecuteAsync(connection, "StatementReadsTvp", customerId: 2);

        var planTypes = await CapturePlanTypesAsync(connection, "StatementReadsTvp");

        Assert.NotEmpty(planTypes);
        Assert.DoesNotContain("Dispatcher Plan", planTypes);
    }

    [Fact]
    public async Task StatementNeverReadingTheTableValuedParameter_StillGetsAParameterSensitivePlanDispatcher()
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(_databaseName));
        await connection.OpenAsync();

        await ExecuteAsync(connection, "StatementIgnoresTvp", customerId: 1);
        await ExecuteAsync(connection, "StatementIgnoresTvp", customerId: 2);

        var planTypes = await CapturePlanTypesAsync(connection, "StatementIgnoresTvp");

        Assert.Contains("Dispatcher Plan", planTypes);
    }
}
