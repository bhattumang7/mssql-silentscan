using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;
using SilentScan.Verify.Catalog;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class HypotheticalIndexOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(HypotheticalIndexOracleTests);

    protected override string Ddl => """
        CREATE TABLE dbo.Parent (Id INT NOT NULL PRIMARY KEY);
        CREATE TABLE dbo.Child (Id INT IDENTITY PRIMARY KEY, ParentId INT NOT NULL CONSTRAINT FK_Child FOREIGN KEY REFERENCES dbo.Parent(Id));
        INSERT INTO dbo.Parent SELECT TOP (1000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) FROM sys.all_objects a, sys.all_objects b;
        INSERT INTO dbo.Child (ParentId) SELECT TOP (20000) (ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) % 999) + 1 FROM sys.all_objects a, sys.all_objects b;
        CREATE INDEX IX_Child_Hypothetical ON dbo.Child(ParentId) WITH STATISTICS_ONLY = -1;
        GO
        CREATE TABLE dbo.Keys (K INT NOT NULL, V INT NOT NULL);
        CREATE UNIQUE INDEX UX_Keys_Hypothetical ON dbo.Keys(K) WITH STATISTICS_ONLY = -1;
        CREATE TABLE dbo.RealKeys (K INT NOT NULL, V INT NOT NULL);
        CREATE UNIQUE INDEX UX_RealKeys ON dbo.RealKeys(K);
        GO
        """;

    [Fact]
    [Trait("Rule", FindingRuleIds.BareTopNoOrderByRuleId)]
    public async Task HypotheticalUniqueIndex_AcceptsDuplicateKeys()
    {
        await ExecuteAsync("INSERT INTO dbo.Keys VALUES (1, 1), (1, 2);");

        Assert.Equal(2, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Keys WHERE K = 1;"));
    }

    [Fact]
    [Trait("Rule", FindingRuleIds.IndexDesignUnindexedForeignKeyRuleId)]
    public async Task ParentDelete_ScansChildWhoseOnlyForeignKeyIndexIsHypothetical()
    {
        await using var connection = await OpenConnectionAsync();
        await ExecuteAsync(connection, "SET SHOWPLAN_XML ON;");
        var plan = System.Xml.Linq.XDocument.Parse((await ScalarAsync<string>(connection, "DELETE FROM dbo.Parent WHERE Id = 1000;"))!);

        Assert.Contains("Clustered Index Scan", ShowPlan.PhysicalOpsOnTable(plan, "Child"));
        Assert.DoesNotContain("IX_Child_Hypothetical", ShowPlan.IndexNames(plan));
    }

    [Fact]
    [Trait("Rule", FindingRuleIds.IndexDesignUnindexedForeignKeyRuleId)]
    public async Task ForeignKeyCoveredOnlyByAHypotheticalIndex_IsReported()
    {
        var catalog = await new LiveCatalogReader(Options.BuildConnectionString(DatabaseName)).ReadAsync();

        var finding = Assert.Single(
            IndexDesignScanner.Scan(catalog), f => f.Kind == IndexDesignFindingKind.UnindexedForeignKey);
        Assert.Equal("dbo.Child", finding.TableQualifiedName);
    }

    [Fact]
    [Trait("Rule", FindingRuleIds.BareTopNoOrderByRuleId)]
    public async Task TopPinnedOnlyByAHypotheticalUniqueIndex_IsReported_RealUniqueIndexControlIsNot()
    {
        var catalog = await new LiveCatalogReader(Options.BuildConnectionString(DatabaseName)).ReadAsync();

        var hypothetical = SqlScriptParser.ParseText("h.sql", "SELECT TOP (1) V FROM dbo.Keys WHERE K = 1;");
        var real = SqlScriptParser.ParseText("r.sql", "SELECT TOP (1) V FROM dbo.RealKeys WHERE K = 1;");

        Assert.Single(BareTopNoOrderByScanner.Scan(hypothetical, catalog));
        Assert.Empty(BareTopNoOrderByScanner.Scan(real, catalog));
    }
}
