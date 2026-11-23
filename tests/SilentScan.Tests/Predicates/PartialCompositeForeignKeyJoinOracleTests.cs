using Microsoft.Data.SqlClient;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.PartialCompositeForeignKeyJoinRuleId)]
public sealed class PartialCompositeForeignKeyJoinOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(PartialCompositeForeignKeyJoinOracleTests);

    protected override string Ddl => """
        CREATE TABLE dbo.Orders (OrderId INT NOT NULL, RevisionId INT NOT NULL, Total DECIMAL(10,2) NOT NULL, CONSTRAINT PK_Orders PRIMARY KEY (OrderId, RevisionId));
        CREATE TABLE dbo.OrderLines (LineId INT IDENTITY PRIMARY KEY, OrderId INT NOT NULL, RevisionId INT NOT NULL, Sku NVARCHAR(50) NOT NULL,
            CONSTRAINT FK_OrderLines_Orders FOREIGN KEY (OrderId, RevisionId) REFERENCES dbo.Orders(OrderId, RevisionId));
        CREATE TABLE dbo.Docs (DocId INT NOT NULL, Rev INT NOT NULL, CONSTRAINT PK_Docs PRIMARY KEY (DocId, Rev));
        CREATE UNIQUE INDEX UX_Docs_DocId_Filtered ON dbo.Docs(DocId) WHERE DocId > 0;
        CREATE TABLE dbo.DocLines (LineId INT IDENTITY PRIMARY KEY, DocId INT NOT NULL, Rev INT NOT NULL,
            CONSTRAINT FK_DocLines_Docs FOREIGN KEY (DocId, Rev) REFERENCES dbo.Docs(DocId, Rev));
        """;

    [Fact]
    public async Task PartialCompositeJoin_FansOneChildRowOutAcrossEveryParentRevision_ButFullCompositeJoinDoesNot()
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(DatabaseName));
        await connection.OpenAsync();

        await using (var seedCommand = new SqlCommand(
            """
            INSERT INTO dbo.Orders (OrderId, RevisionId, Total) VALUES (1, 1, 100.00), (1, 2, 150.00);
            INSERT INTO dbo.OrderLines (OrderId, RevisionId, Sku) VALUES (1, 1, 'WIDGET-A');
            """, connection))
        {
            await seedCommand.ExecuteNonQueryAsync();
        }

        await using (var partialJoinCommand = new SqlCommand(
            "SELECT COUNT(*) FROM dbo.OrderLines ol JOIN dbo.Orders o ON ol.OrderId = o.OrderId;", connection))
        {
            var partialCount = (int)(await partialJoinCommand.ExecuteScalarAsync())!;
            Assert.Equal(2, partialCount);
        }

        await using (var fullCompositeJoinCommand = new SqlCommand(
            "SELECT COUNT(*) FROM dbo.OrderLines ol JOIN dbo.Orders o ON ol.OrderId = o.OrderId AND ol.RevisionId = o.RevisionId;", connection))
        {
            var fullCount = (int)(await fullCompositeJoinCommand.ExecuteScalarAsync())!;
            Assert.Equal(1, fullCount);
        }
    }

    [Fact]
    public async Task FilteredUniqueIndexOnTheJoinedColumns_StillLetsAPartialJoinFanOut()
    {
        await ExecuteAsync("INSERT INTO dbo.Docs (DocId, Rev) VALUES (0, 1), (0, 2); INSERT INTO dbo.DocLines (DocId, Rev) VALUES (0, 1);");

        Assert.Equal(2, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.DocLines dl JOIN dbo.Docs d ON dl.DocId = d.DocId;"));
    }
}
