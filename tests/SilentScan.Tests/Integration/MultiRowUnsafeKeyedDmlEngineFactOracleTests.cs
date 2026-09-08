using Microsoft.Data.SqlClient;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class MultiRowUnsafeKeyedDmlEngineFactOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(MultiRowUnsafeKeyedDmlEngineFactOracleTests);

    protected override string Ddl => """
        CREATE TABLE dbo.Products (ProductId INT NOT NULL PRIMARY KEY, Touched BIT NOT NULL DEFAULT 0);
        INSERT INTO dbo.Products (ProductId, Touched) VALUES (1, 0), (2, 0), (3, 0);
        GO
        CREATE TABLE dbo.StockMoves (ProductId INT NOT NULL);
        GO
        CREATE TRIGGER dbo.trg_StockMoves_Insert ON dbo.StockMoves AFTER INSERT AS
        BEGIN
            DECLARE @productId INT;
            SELECT @productId = ProductId FROM inserted;
            UPDATE dbo.Products SET Touched = 1 WHERE ProductId = @productId;
        END;
        GO
        """;

    [Fact]
    public async Task KeyedUpdateDrivenByCapturedVariable_SucceedsButTouchesOnlyOneOfTheThreeInsertedRows()
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(DatabaseName));
        await connection.OpenAsync();

        await using var insertCommand = connection.CreateCommand();
        insertCommand.CommandText = "INSERT INTO dbo.StockMoves (ProductId) VALUES (1), (2), (3);";
        await insertCommand.ExecuteNonQueryAsync();

        await using var touchedCountCommand = connection.CreateCommand();
        touchedCountCommand.CommandText = "SELECT COUNT(*) FROM dbo.Products WHERE Touched = 1;";
        var touchedCount = (int)(await touchedCountCommand.ExecuteScalarAsync())!;
        Assert.Equal(1, touchedCount);
    }
}
