using Microsoft.Data.SqlClient;
using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;
using SilentScan.Verify.Catalog;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.IndexDesignDuplicateIndexRuleId)]
[Trait("Rule", FindingRuleIds.IndexDesignSubsumedIndexRuleId)]
public sealed class IndexDesignDuplicateIndexOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(IndexDesignDuplicateIndexOracleTests);

    protected override string Ddl => """
        CREATE TABLE dbo.Plain (Id INT NOT NULL, A INT NOT NULL, B INT NOT NULL);
        CREATE INDEX IX_Plain_1 ON dbo.Plain(A);
        CREATE INDEX IX_Plain_2 ON dbo.Plain(A);
        GO
        CREATE TABLE dbo.Geo (Id INT NOT NULL PRIMARY KEY, G GEOGRAPHY NOT NULL);
        CREATE SPATIAL INDEX SX_1 ON dbo.Geo(G) USING GEOGRAPHY_AUTO_GRID;
        CREATE SPATIAL INDEX SX_2 ON dbo.Geo(G) USING GEOGRAPHY_GRID WITH (GRIDS = (HIGH, HIGH, HIGH, HIGH));
        GO
        CREATE TABLE dbo.Doc (Id INT NOT NULL PRIMARY KEY, X XML NOT NULL);
        CREATE PRIMARY XML INDEX PX ON dbo.Doc(X);
        CREATE XML INDEX XP ON dbo.Doc(X) USING XML INDEX PX FOR PATH;
        CREATE XML INDEX XV ON dbo.Doc(X) USING XML INDEX PX FOR VALUE;
        GO
        CREATE TABLE dbo.Clus (Id INT NOT NULL, A INT NOT NULL, B INT NOT NULL);
        CREATE CLUSTERED INDEX CX_Clus ON dbo.Clus(A);
        CREATE INDEX IX_Clus ON dbo.Clus(A);
        CREATE TABLE dbo.ClusPrefix (Id INT NOT NULL, A INT NOT NULL, B INT NOT NULL);
        CREATE CLUSTERED INDEX CX_ClusPrefix ON dbo.ClusPrefix(A);
        CREATE INDEX IX_ClusPrefix ON dbo.ClusPrefix(A, B);
        GO
        CREATE TABLE dbo.UniqueKeys (Id INT NOT NULL, A INT NOT NULL, B INT NOT NULL);
        CREATE UNIQUE INDEX UX_Narrow ON dbo.UniqueKeys(A);
        CREATE UNIQUE INDEX UX_Wide ON dbo.UniqueKeys(A, B);
        GO
        """;

    private async Task<IReadOnlyList<IndexDesignFinding>> ScanLiveAsync()
    {
        var catalog = await new LiveCatalogReader(Options.BuildConnectionString(DatabaseName)).ReadAsync();
        return IndexDesignScanner.Scan(catalog);
    }

    [Fact]
    public async Task TwoPlainNonclusteredIndexesOnTheSameColumn_AreReportedAsDuplicates()
    {
        var findings = await ScanLiveAsync();

        Assert.Contains(findings, f => f.Kind == IndexDesignFindingKind.DuplicateIndex && f.TableQualifiedName == "dbo.Plain");
    }

    [Theory]
    [InlineData("dbo.Geo")]
    [InlineData("dbo.Doc")]
    [InlineData("dbo.Clus")]
    [InlineData("dbo.ClusPrefix")]
    [InlineData("dbo.UniqueKeys")]
    public async Task IndexesTheEngineDoesNotTreatAsInterchangeable_AreNeverReportedAsRedundant(string table)
    {
        var findings = await ScanLiveAsync();

        Assert.DoesNotContain(findings, f => f.TableQualifiedName == table
            && f.Kind is IndexDesignFindingKind.DuplicateIndex or IndexDesignFindingKind.SubsumedIndex);
    }

    [Fact]
    public async Task UniqueNarrowIndex_RejectsADuplicateTheWiderUniqueIndexAccepts()
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(DatabaseName));
        await connection.OpenAsync();

        await using (var seed = new SqlCommand("INSERT dbo.UniqueKeys (Id, A, B) VALUES (1, 1, 1);", connection))
        {
            await seed.ExecuteNonQueryAsync();
        }

        await using var duplicate = new SqlCommand("INSERT dbo.UniqueKeys (Id, A, B) VALUES (2, 1, 2);", connection);
        var error = await Assert.ThrowsAsync<SqlException>(() => duplicate.ExecuteNonQueryAsync());

        Assert.Contains("UX_Narrow", error.Message, StringComparison.Ordinal);
    }
}
