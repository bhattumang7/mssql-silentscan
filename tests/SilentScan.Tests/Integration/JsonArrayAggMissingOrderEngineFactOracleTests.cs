using Microsoft.Data.SqlClient;
using SilentScan.Verify;
using SilentScan.Verify.Deployment;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class JsonArrayAggMissingOrderEngineFactOracleTests : IAsyncLifetime
{
    private static readonly SqlServerOptions Options = new(
        Host: "localhost",
        Port: int.TryParse(Environment.GetEnvironmentVariable("SILENTSCAN_SQL2025_PORT"), out var port) ? port : 14331,
        UserId: "sa",
        Password: Environment.GetEnvironmentVariable("SILENTSCAN_SA_PASSWORD") ?? "SilentScan!Dev2026");

    private readonly string _databaseName = $"{nameof(JsonArrayAggMissingOrderEngineFactOracleTests)}_{Guid.NewGuid():N}";

    private const string Ddl = """
        CREATE TABLE dbo.Member (GroupId INT NOT NULL, Name VARCHAR(50) NOT NULL);
        INSERT INTO dbo.Member (GroupId, Name) VALUES (1,'Charlie'),(1,'Alice'),(1,'Bob');
        GO
        """;

    public async Task InitializeAsync()
    {
        await new DatabaseProvisioner(Options).CreateFreshAsync(_databaseName);
        await new ScriptDeployer(Options).DeployAsync(Ddl, _databaseName);
    }

    public async Task DisposeAsync() =>
        await new DatabaseProvisioner(Options).DropIfExistsAsync(_databaseName);

    [Fact]
    public async Task SameQueryTextWithNoOrderByInsideTheCall_ChangesArrayOrderWhenAnIndexRemovesTheSort()
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(_databaseName));
        await connection.OpenAsync();

        const string query = "SELECT JSON_ARRAYAGG(Name) FROM dbo.Member GROUP BY GroupId;";

        await using var heapCommand = connection.CreateCommand();
        heapCommand.CommandText = query;
        var heapOrderResult = (string)(await heapCommand.ExecuteScalarAsync())!;
        Assert.Equal("""["Charlie","Alice","Bob"]""", heapOrderResult);

        await using var indexCommand = connection.CreateCommand();
        indexCommand.CommandText = """
            CREATE INDEX IX_Member_GroupId_Name ON dbo.Member(GroupId, Name);
            UPDATE STATISTICS dbo.Member WITH FULLSCAN;
            """;
        await indexCommand.ExecuteNonQueryAsync();

        await using var indexedCommand = connection.CreateCommand();
        indexedCommand.CommandText = query;
        var indexedOrderResult = (string)(await indexedCommand.ExecuteScalarAsync())!;
        Assert.Equal("""["Alice","Bob","Charlie"]""", indexedOrderResult);
    }

    [Fact]
    public async Task WithinGroupOrderBy_CompilesWithNoErrorButHasNoEffectOnArrayOrder()
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(_databaseName));
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT JSON_ARRAYAGG(Name) WITHIN GROUP (ORDER BY Name) FROM dbo.Member GROUP BY GroupId;";
        var result = (string)(await command.ExecuteScalarAsync())!;

        Assert.Equal("""["Charlie","Alice","Bob"]""", result);
    }
}
