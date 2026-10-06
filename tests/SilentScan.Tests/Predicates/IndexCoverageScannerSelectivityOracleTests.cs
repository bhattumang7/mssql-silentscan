using SilentScan.Core.Catalog;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", "silentscan/index/key-lookup-prone")]
public sealed class IndexCoverageScannerSelectivityOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(IndexCoverageScannerSelectivityOracleTests);

    protected override string Ddl => """
        CREATE TABLE dbo.HeapT (Id INT NOT NULL, A INT NOT NULL, CONSTRAINT PK_HeapT PRIMARY KEY NONCLUSTERED (Id));
        CREATE NONCLUSTERED INDEX IX_HeapT_A ON dbo.HeapT(A);
        CREATE TABLE dbo.FlagT (Id INT NOT NULL PRIMARY KEY, Flag BIT NOT NULL, Payload INT NOT NULL);
        CREATE NONCLUSTERED INDEX IX_FlagT_Flag ON dbo.FlagT(Flag);
        GO
        """;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await ExecuteAsync(
            """
            INSERT INTO dbo.HeapT (Id, A)
            SELECT TOP (5000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)),
                   ROW_NUMBER() OVER (ORDER BY (SELECT NULL))
            FROM sys.all_objects a CROSS JOIN sys.all_objects b;

            UPDATE STATISTICS dbo.HeapT WITH FULLSCAN;

            INSERT INTO dbo.FlagT (Id, Flag, Payload)
            SELECT TOP (5000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)),
                   CASE WHEN ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) = 1 THEN 1 ELSE 0 END,
                   ROW_NUMBER() OVER (ORDER BY (SELECT NULL))
            FROM sys.all_objects a CROSS JOIN sys.all_objects b;

            UPDATE STATISTICS dbo.FlagT WITH FULLSCAN;
            """);
    }

    private const string StaticDdl =
        "CREATE TABLE dbo.HeapT (Id INT NOT NULL, A INT NOT NULL, CONSTRAINT PK_HeapT PRIMARY KEY NONCLUSTERED (Id));"
        + "CREATE NONCLUSTERED INDEX IX_HeapT_A ON dbo.HeapT(A);"
        + "CREATE TABLE dbo.FlagT (Id INT NOT NULL PRIMARY KEY, Flag BIT NOT NULL, Payload INT NOT NULL);"
        + "CREATE NONCLUSTERED INDEX IX_FlagT_Flag ON dbo.FlagT(Flag);";

    private static IReadOnlyList<IndexCoverageFinding> Scan(string query)
    {
        var result = SqlScriptParser.ParseText("test.sql", $"{StaticDdl}\nGO\n{query}");
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));

        var catalog = CatalogBuilder.Build([result]);
        return IndexCoverageScanner.Scan(result, catalog);
    }

    [Fact]
    public async Task UnselectiveRangePredicate_OptimizerPicksTableScanNotLookup_ScannerDoesNotFlagIt()
    {
        const string Query = "SELECT Id, A FROM dbo.HeapT WHERE A >= 0;";

        var plan = await PlanInSessionAsync(string.Empty, Query);
        var lookupPresent = plan.Descendants().Any(e => e.Name.LocalName == "RelOp" && (string?)e.Attribute("PhysicalOp") == "RID Lookup");

        Assert.False(lookupPresent, "expected the optimizer to prefer a scan over a seek+lookup for an unselective predicate matching every row");

        var findings = Scan(Query);
        Assert.DoesNotContain(findings, f => f.Kind == IndexCoverageFindingKind.KeyLookupProneIndex);
    }

    [Fact]
    public async Task EqualityPredicate_OptimizerStillPicksSeekPlusLookup_ScannerFlagsIt()
    {
        const string Query = "SELECT Id, A FROM dbo.HeapT WHERE A = 5;";

        var plan = await PlanInSessionAsync(string.Empty, Query);
        var lookupPresent = plan.Descendants().Any(e => e.Name.LocalName == "RelOp" && (string?)e.Attribute("PhysicalOp") == "RID Lookup");

        Assert.True(lookupPresent, "expected the optimizer to seek the selective index and follow up with an RID lookup");

        var findings = Scan(Query);
        Assert.Single(findings, f => f.Kind == IndexCoverageFindingKind.KeyLookupProneIndex);
    }

    [Fact]
    public async Task EqualityOnBitLeadingKeyMatchingMostRows_OptimizerScansWithoutKeyLookup_ScannerDoesNotFlagIt()
    {
        const string Query = "SELECT Id, Flag, Payload FROM dbo.FlagT WHERE Flag = 0;";

        var plan = await PlanInSessionAsync(string.Empty, Query);
        var lookupPresent = plan.Descendants().Any(e => e.Name.LocalName == "IndexScan" && (string?)e.Attribute("Lookup") == "1");

        Assert.False(lookupPresent, "expected a scan, not a seek plus Key Lookup, for a bit flag equal to the value nearly every row holds");

        var rareValuePlan = await PlanInSessionAsync(string.Empty, "SELECT Id, Flag, Payload FROM dbo.FlagT WHERE Flag = 1;");
        Assert.Contains(rareValuePlan.Descendants(), e => e.Name.LocalName == "IndexScan" && (string?)e.Attribute("Lookup") == "1");

        var findings = Scan(Query);
        Assert.DoesNotContain(findings, f => f.Kind == IndexCoverageFindingKind.KeyLookupProneIndex);
    }
}
