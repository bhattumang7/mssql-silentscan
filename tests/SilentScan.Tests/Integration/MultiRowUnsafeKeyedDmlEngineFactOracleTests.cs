using SilentScan.Tests.Support;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
[Trait("Rule", "silentscan/trigger/multi-row-unsafe-keyed-dml")]
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
        CREATE TABLE dbo.SetBasedProducts (ProductId INT NOT NULL PRIMARY KEY, Touched BIT NOT NULL DEFAULT 0);
        INSERT INTO dbo.SetBasedProducts (ProductId, Touched) VALUES (1, 0), (2, 0), (3, 0);
        GO
        CREATE TABLE dbo.SetBasedStockMoves (ProductId INT NOT NULL);
        GO
        CREATE TRIGGER dbo.trg_SetBasedStockMoves_Insert ON dbo.SetBasedStockMoves AFTER INSERT AS
        BEGIN
            UPDATE p SET Touched = 1 FROM dbo.SetBasedProducts p JOIN inserted i ON i.ProductId = p.ProductId;
        END;
        GO
        """;

    [Fact]
    public async Task KeyedUpdateDrivenByCapturedVariable_SucceedsButTouchesOnlyOneOfTheThreeInsertedRows_SetBasedControlTouchesAllThree()
    {
        await ExecuteAsync("INSERT INTO dbo.StockMoves (ProductId) VALUES (1), (2), (3); INSERT INTO dbo.SetBasedStockMoves (ProductId) VALUES (1), (2), (3);");

        var touchedByCapturedVariable = await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Products WHERE Touched = 1;");
        var touchedBySetBased = await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.SetBasedProducts WHERE Touched = 1;");

        Assert.Equal(1, touchedByCapturedVariable);
        Assert.Equal(3, touchedBySetBased);
    }
}
