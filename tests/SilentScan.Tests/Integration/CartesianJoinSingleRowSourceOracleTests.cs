using Microsoft.Data.SqlClient;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.CartesianJoinCommaJoinRuleId)]
[Trait("Rule", FindingRuleIds.CartesianJoinExplicitCrossJoinRuleId)]
public sealed class CartesianJoinSingleRowSourceOracleTests : OracleTestFixture
{
    private const int OrderCount = 40;

    protected override string DatabaseNameSeed => nameof(CartesianJoinSingleRowSourceOracleTests);

    protected override string Ddl => $"""
        CREATE TABLE dbo.Config (Id INT NOT NULL PRIMARY KEY, Val INT NOT NULL);
        INSERT dbo.Config (Id, Val) VALUES (1, 10), (2, 20), (3, 30);
        CREATE TABLE dbo.Orders (OrderId INT NOT NULL PRIMARY KEY, Amount INT NOT NULL);
        INSERT dbo.Orders (OrderId, Amount) SELECT TOP ({OrderCount}) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)), 1 FROM sys.all_objects;
        """;

    private async Task<int> RowCountAsync(string statement)
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(DatabaseName));
        await connection.OpenAsync();
        await using var command = new SqlCommand($"SELECT COUNT(*) FROM ({statement}) q;", connection);
        return (int)(await command.ExecuteScalarAsync())!;
    }

    [Theory]
    [InlineData("SELECT o.OrderId FROM dbo.Config c, dbo.Orders o WHERE c.Id = 1")]
    [InlineData("SELECT o.OrderId FROM dbo.Config c CROSS JOIN dbo.Orders o WHERE c.Id = 2")]
    public async Task CrossJoinWithAKeyPinnedTable_ReturnsExactlyTheOtherTablesRows(string statement)
    {
        Assert.Equal(OrderCount, await RowCountAsync(statement));
    }

    [Theory]
    [InlineData("SELECT o.OrderId FROM dbo.Config c, dbo.Orders o")]
    [InlineData("SELECT o.OrderId FROM dbo.Config c CROSS JOIN dbo.Orders o WHERE c.Id > 1")]
    public async Task CrossJoinWithoutASingleRowPin_MultipliesTheRows(string statement)
    {
        Assert.True(await RowCountAsync(statement) > OrderCount);
    }
}
