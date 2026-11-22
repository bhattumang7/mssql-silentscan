using Microsoft.Data.SqlClient;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.StringAggMissingOrderRuleId)]
public sealed class StringAggMissingOrderEngineFactOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(StringAggMissingOrderEngineFactOracleTests);

    protected override string Ddl => """
        CREATE TABLE dbo.Member (GroupId INT NOT NULL, Name VARCHAR(50) NOT NULL);
        INSERT INTO dbo.Member (GroupId, Name) VALUES (1,'Charlie'),(1,'Alice'),(1,'Bob');
        GO
        """;

    private const string Query = "SELECT STRING_AGG(Name, ',') FROM dbo.Member GROUP BY GroupId;";

    [Fact]
    public async Task StringAggOverAConstant_ReturnsTheSameStringWhicheverOrderTheRowsArrive()
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = new SqlCommand(
            "SELECT (SELECT STRING_AGG('x', ',') FROM (SELECT TOP (100) PERCENT Name FROM dbo.Member ORDER BY Name ASC) a), (SELECT STRING_AGG('x', ',') FROM (SELECT TOP (100) PERCENT Name FROM dbo.Member ORDER BY Name DESC) d);",
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();

        Assert.Equal("x,x,x", reader.GetString(0));
        Assert.Equal(reader.GetString(0), reader.GetString(1));
    }

    [Fact]
    public async Task SameQueryTextWithNoWithinGroup_ChangesConcatenationOrderWhenAnIndexRemovesTheSort()
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(DatabaseName));
        await connection.OpenAsync();

        await using var heapCommand = connection.CreateCommand();
        heapCommand.CommandText = Query;
        var heapOrderResult = (string)(await heapCommand.ExecuteScalarAsync())!;
        Assert.Equal("Charlie,Alice,Bob", heapOrderResult);

        await using var indexCommand = connection.CreateCommand();
        indexCommand.CommandText = """
            CREATE INDEX IX_Member_GroupId_Name ON dbo.Member(GroupId, Name);
            UPDATE STATISTICS dbo.Member WITH FULLSCAN;
            """;
        await indexCommand.ExecuteNonQueryAsync();

        await using var indexedCommand = connection.CreateCommand();
        indexedCommand.CommandText = Query;
        var indexedOrderResult = (string)(await indexedCommand.ExecuteScalarAsync())!;
        Assert.Equal("Alice,Bob,Charlie", indexedOrderResult);
    }
}
