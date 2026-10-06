using System.Xml.Linq;
using SilentScan.Core.Catalog;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", "silentscan/index/key-lookup-prone")]
public sealed class IndexCoverageScannerUniqueFullKeyOracleTests : OracleTestFixture
{
    private const string StaticDdl =
        "CREATE TABLE dbo.Items (Id INT NOT NULL PRIMARY KEY CLUSTERED, Code INT NOT NULL, Grp INT NOT NULL, A INT NOT NULL, B INT NOT NULL, Payload INT NOT NULL);"
        + "CREATE UNIQUE NONCLUSTERED INDEX UX_Items_Code ON dbo.Items(Code);"
        + "CREATE NONCLUSTERED INDEX IX_Items_Grp ON dbo.Items(Grp);"
        + "CREATE UNIQUE NONCLUSTERED INDEX UX_Items_AB ON dbo.Items(A, B);";

    protected override string DatabaseNameSeed => nameof(IndexCoverageScannerUniqueFullKeyOracleTests);

    protected override string Ddl => StaticDdl.Replace(";", ";\nGO\n", StringComparison.Ordinal);

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await ExecuteAsync(
            """
            INSERT INTO dbo.Items (Id, Code, Grp, A, B, Payload)
            SELECT TOP (20000) n, n, n % 1000, n % 1000, n, n
            FROM (SELECT ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n FROM sys.all_objects a CROSS JOIN sys.all_objects b) x;

            UPDATE STATISTICS dbo.Items WITH FULLSCAN;
            """);
    }

    private static IReadOnlyList<IndexCoverageFinding> Scan(string query)
    {
        var result = SqlScriptParser.ParseText("test.sql", $"{StaticDdl}\nGO\n{query}");
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));

        var catalog = CatalogBuilder.Build([result]);
        return IndexCoverageScanner.Scan(result, catalog);
    }

    private async Task<long?> KeyLookupActualRowsAsync(string query)
    {
        var plan = XDocument.Parse(await CaptureActualPlanAsync(query));
        var lookups = plan.Descendants()
            .Where(e => e.Name.LocalName == "IndexScan" && (string?)e.Attribute("Lookup") == "1")
            .Select(e => e.Parent!)
            .ToList();
        if (lookups.Count == 0)
        {
            return null;
        }

        return lookups
            .SelectMany(r => r.Elements().Where(c => c.Name.LocalName == "RunTimeInformation"))
            .SelectMany(r => r.Elements().Where(c => c.Name.LocalName == "RunTimeCountersPerThread"))
            .Sum(c => long.Parse((string)c.Attribute("ActualRows")!, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task EqualityOnFullKeyOfUniqueIndex_LookupReadsOneRow_ScannerDoesNotFlagIt()
    {
        const string Query = "SELECT Payload FROM dbo.Items WHERE Code = 5;";

        var rows = await KeyLookupActualRowsAsync(Query);
        Assert.Equal(1L, rows);
        Assert.DoesNotContain(Scan(Query), f => f.Kind == IndexCoverageFindingKind.KeyLookupProneIndex);
    }

    [Fact]
    public async Task EqualityOnFullCompositeKeyOfUniqueIndex_LookupReadsOneRow_ScannerDoesNotFlagIt()
    {
        const string Query = "SELECT Payload FROM dbo.Items WHERE A = 5 AND B = 1005;";

        var rows = await KeyLookupActualRowsAsync(Query);
        Assert.Equal(1L, rows);
        Assert.DoesNotContain(Scan(Query), f => f.Kind == IndexCoverageFindingKind.KeyLookupProneIndex);
    }

    [Fact]
    public async Task EqualityOnNonUniqueIndexKey_LookupReadsManyRows_ScannerFlagsIt()
    {
        const string Query = "SELECT Payload FROM dbo.Items WHERE Grp = 5;";

        var rows = await KeyLookupActualRowsAsync(Query);
        Assert.True(rows > 1, $"lookup rows: {rows}");
        Assert.Single(Scan(Query), f => f.Kind == IndexCoverageFindingKind.KeyLookupProneIndex);
    }

    [Fact]
    public async Task EqualityOnPartialKeyOfUniqueIndex_LookupReadsManyRows_ScannerFlagsIt()
    {
        const string Query = "SELECT Payload FROM dbo.Items WHERE A = 5;";

        var rows = await KeyLookupActualRowsAsync(Query);
        Assert.True(rows > 1, $"lookup rows: {rows}");
        Assert.Single(Scan(Query), f => f.Kind == IndexCoverageFindingKind.KeyLookupProneIndex);
    }
}
