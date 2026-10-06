using SilentScan.Core.Catalog;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", "silentscan/tier1/function-wrapped-column")]
[Trait("Rule", "silentscan/tier1/date-function-on-column")]
public sealed class NonSargableWrapSeekAvailabilityOracleTests : OracleTestFixture
{
    private const string StaticDdl =
        "CREATE TABLE dbo.Wrap (Id INT NOT NULL PRIMARY KEY CLUSTERED, Idx INT NOT NULL, NonKey INT NOT NULL, D DATETIME NOT NULL);"
        + "CREATE NONCLUSTERED INDEX IX_Wrap_Idx ON dbo.Wrap(Idx);"
        + "CREATE NONCLUSTERED INDEX IX_Wrap_D ON dbo.Wrap(D);"
        + "\nGO\nCREATE VIEW dbo.WrapView AS SELECT Id, Idx, NonKey, D FROM dbo.Wrap UNION ALL SELECT Id, Idx, NonKey, D FROM dbo.Wrap;";

    protected override string DatabaseNameSeed => nameof(NonSargableWrapSeekAvailabilityOracleTests);

    protected override string Ddl => StaticDdl.Replace(";", ";\nGO\n", StringComparison.Ordinal);

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await ExecuteAsync(
            """
            INSERT INTO dbo.Wrap (Id, Idx, NonKey, D)
            SELECT TOP (50000) n, n, n, DATEADD(DAY, n % 2000, '20200101')
            FROM (SELECT ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n FROM sys.all_objects a CROSS JOIN sys.all_objects b) x;

            UPDATE STATISTICS dbo.Wrap WITH FULLSCAN;
            """);
    }

    private static IReadOnlyList<SargabilityFinding> Scan(string query)
    {
        var result = SqlScriptParser.ParseText("test.sql", $"{StaticDdl}\nGO\n{query}");
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));

        var catalog = CatalogBuilder.Build([result]);
        var lineage = LineageResolver.Resolve(catalog, [result]);
        return NonSargablePredicateScanner.Scan(result, catalog, lineage);
    }

    private async Task<bool> SeeksAsync(string query)
    {
        var plan = await PlanInSessionAsync(string.Empty, query);
        return plan.Descendants().Any(e => e.Name.LocalName == "RelOp"
            && ((string?)e.Attribute("PhysicalOp"))?.Contains("Seek", StringComparison.Ordinal) == true
            && e.Descendants().Any(s => s.Name.LocalName == "SeekPredicates"));
    }

    [Fact]
    public async Task WrappingColumnInNoIndexKey_PlanIsUnchangedByTheWrap_ScannerDoesNotReportItAtHighConfidence()
    {
        const string Bare = "SELECT Id FROM dbo.Wrap WHERE NonKey = 5;";
        const string Wrapped = "SELECT Id FROM dbo.Wrap WHERE ABS(NonKey) = 5;";

        Assert.False(await SeeksAsync(Bare));
        Assert.False(await SeeksAsync(Wrapped));
        Assert.DoesNotContain(Scan(Wrapped), f => f.Confidence == FindingConfidence.High);
    }

    [Fact]
    public async Task WrappingColumnLeadingAnIndexKey_WrapLosesTheSeek_ScannerReportsItAtHighConfidence()
    {
        const string Bare = "SELECT Id FROM dbo.Wrap WHERE Idx = 5;";
        const string Wrapped = "SELECT Id FROM dbo.Wrap WHERE ABS(Idx) = 5;";

        Assert.True(await SeeksAsync(Bare));
        Assert.False(await SeeksAsync(Wrapped));
        Assert.Single(Scan(Wrapped), f => f.Confidence == FindingConfidence.High);
    }

    [Fact]
    public async Task WrappedPredicateBesideBareRangeOnSameColumn_PlanStillSeeks_ScannerDoesNotReportIt()
    {
        const string Query =
            "SELECT Id FROM dbo.Wrap WHERE D BETWEEN '20200101' AND '20200103' AND DATEPART(dw, D) = 2;";

        Assert.True(await SeeksAsync(Query));
        Assert.Empty(Scan(Query));
    }

    [Fact]
    public async Task WrappedPredicateAloneOnIndexedColumn_PlanDoesNotSeek_ScannerReportsIt()
    {
        const string Query = "SELECT Id FROM dbo.Wrap WHERE DATEPART(dw, D) = 2;";

        Assert.False(await SeeksAsync(Query));
        Assert.Single(Scan(Query), f => f.Confidence == FindingConfidence.High);
    }

    [Fact]
    public async Task WrappedPredicateInJoinOnBesideBareEqualityInWhere_PlanStillSeeks_ScannerDoesNotReportIt()
    {
        const string Query =
            "SELECT a.Id FROM dbo.Wrap a JOIN dbo.Wrap b ON b.Id = a.Id AND DATEPART(dw, a.D) = 2 WHERE a.D = '20200101';";

        Assert.True(await SeeksAsync(Query));
        Assert.DoesNotContain(Scan(Query), f => f.ColumnName == "D" && f.Confidence == FindingConfidence.High);
    }

    [Fact]
    public async Task BareEqualityOnSameColumnInsideOrWithWrappedPredicate_PlanDoesNotSeek_ScannerStillReportsIt()
    {
        const string Query = "SELECT Id FROM dbo.Wrap WHERE D = '20200101' OR DATEPART(dw, D) = 2;";

        Assert.False(await SeeksAsync(Query));
        Assert.Single(Scan(Query), f => f.Confidence == FindingConfidence.High);
    }

    [Fact]
    public async Task WrappingNonKeyColumnReachedThroughUnionView_PlanIsUnchangedByTheWrap_ScannerDoesNotReportItAtHighConfidence()
    {
        const string Bare = "SELECT Id FROM dbo.WrapView WHERE NonKey = 5;";
        const string Wrapped = "SELECT Id FROM dbo.WrapView WHERE ABS(NonKey) = 5;";

        Assert.False(await SeeksAsync(Bare));
        Assert.False(await SeeksAsync(Wrapped));
        Assert.DoesNotContain(Scan(Wrapped), f => f.Confidence == FindingConfidence.High);
    }
}
