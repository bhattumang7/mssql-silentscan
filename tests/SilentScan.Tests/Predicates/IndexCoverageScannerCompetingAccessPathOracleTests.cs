using SilentScan.Core.Catalog;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", "silentscan/index/key-lookup-prone")]
public sealed class IndexCoverageScannerCompetingAccessPathOracleTests : OracleTestFixture
{
    private const string StaticDdl =
        "CREATE TABLE dbo.Trips (Id INT NOT NULL, AgencyId INT NOT NULL, Funding INT NOT NULL, Payload INT NOT NULL, CONSTRAINT PK_Trips PRIMARY KEY CLUSTERED (Id, AgencyId));"
        + "CREATE NONCLUSTERED INDEX IX_Trips_Funding ON dbo.Trips(Funding);"
        + "CREATE TABLE dbo.Big (Id INT NOT NULL PRIMARY KEY, Fk INT NOT NULL);";

    protected override string DatabaseNameSeed => nameof(IndexCoverageScannerCompetingAccessPathOracleTests);

    protected override string Ddl => StaticDdl.Replace(";", ";\nGO\n", StringComparison.Ordinal);

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await ExecuteAsync(
            """
            INSERT INTO dbo.Trips (Id, AgencyId, Funding, Payload)
            SELECT TOP (20000) n, 1, n % 1000, n
            FROM (SELECT ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n FROM sys.all_objects a CROSS JOIN sys.all_objects b) x;

            INSERT INTO dbo.Big (Id, Fk)
            SELECT TOP (20000) n, n % 1000
            FROM (SELECT ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n FROM sys.all_objects a CROSS JOIN sys.all_objects b) x;

            UPDATE STATISTICS dbo.Trips WITH FULLSCAN;
            UPDATE STATISTICS dbo.Big WITH FULLSCAN;
            """);
    }

    private static IReadOnlyList<IndexCoverageFinding> Scan(string query)
    {
        var result = SqlScriptParser.ParseText("test.sql", $"{StaticDdl}\nGO\n{query}");
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));

        var catalog = CatalogBuilder.Build([result]);
        return IndexCoverageScanner.Scan(result, catalog);
    }

    private async Task<bool> KeyLookupInPlanAsync(string query)
    {
        var plan = await PlanInSessionAsync(string.Empty, query);
        return plan.Descendants().Any(e => e.Name.LocalName == "IndexScan" && (string?)e.Attribute("Lookup") == "1");
    }

    [Fact]
    public async Task EqualityOnNonclusteredKeyAlone_PlanHasKeyLookup_ScannerFlagsIt()
    {
        const string Query = "SELECT Payload FROM dbo.Trips WHERE Funding = 5;";

        Assert.True(await KeyLookupInPlanAsync(Query));
        Assert.Single(Scan(Query), f => f.Kind == IndexCoverageFindingKind.KeyLookupProneIndex);
    }

    [Fact]
    public async Task EqualityAlsoOnClusteredLeadingKey_PlanSeeksClusteredIndexWithoutLookup_ScannerDoesNotFlagIt()
    {
        const string Query = "SELECT Payload FROM dbo.Trips WHERE Id = 7 AND Funding = 5;";

        Assert.False(await KeyLookupInPlanAsync(Query));
        Assert.DoesNotContain(Scan(Query), f => f.Kind == IndexCoverageFindingKind.KeyLookupProneIndex);
    }

    [Fact]
    public async Task JoinEqualityBetweenColumnsOnly_PlanScansWithoutLookup_ScannerDoesNotFlagIt()
    {
        const string Query = "SELECT t.Payload FROM dbo.Big b JOIN dbo.Trips t ON t.Funding = b.Fk;";

        Assert.False(await KeyLookupInPlanAsync(Query));
        Assert.DoesNotContain(Scan(Query), f => f.Kind == IndexCoverageFindingKind.KeyLookupProneIndex);
    }

    [Fact]
    public async Task JoinOnClauseEqualityAgainstConstant_PlanHasKeyLookup_ScannerFlagsIt()
    {
        const string Query = "SELECT t.Payload FROM dbo.Big b JOIN dbo.Trips t ON t.Funding = 5 AND t.Payload = b.Id;";

        Assert.True(await KeyLookupInPlanAsync(Query));
        Assert.Single(Scan(Query), f => f.Kind == IndexCoverageFindingKind.KeyLookupProneIndex);
    }
}
