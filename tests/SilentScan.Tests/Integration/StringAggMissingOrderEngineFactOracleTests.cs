using Microsoft.Data.SqlClient;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
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
