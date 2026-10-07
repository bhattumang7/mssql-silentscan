using SilentScan.Core.Catalog;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.Tier1FunctionWrappedColumnRuleId)]
[Trait("Rule", FindingRuleIds.Tier1DateFunctionOnColumnRuleId)]
[Trait("Rule", FindingRuleIds.Tier1ColumnArithmeticRuleId)]
[Trait("Rule", FindingRuleIds.Tier1CastOrConvertOnColumnRuleId)]
[Trait("Rule", FindingRuleIds.Tier1CaseFoldOnColumnRuleId)]
public sealed class NonSargableWrapSeekAvailabilityOracleTests : OracleTestFixture
{
    private const string StaticDdl =
        "CREATE TABLE dbo.Wrap (Id INT NOT NULL PRIMARY KEY CLUSTERED, Idx INT NOT NULL, NonKey INT NOT NULL, D DATETIME NOT NULL);"
        + "CREATE NONCLUSTERED INDEX IX_Wrap_Idx ON dbo.Wrap(Idx);"
        + "CREATE NONCLUSTERED INDEX IX_Wrap_D ON dbo.Wrap(D);"
        + "CREATE TABLE dbo.WrapPair (Id INT NOT NULL PRIMARY KEY CLUSTERED, A INT NOT NULL, B INT NOT NULL, Pad CHAR(200) NOT NULL);"
        + "CREATE NONCLUSTERED INDEX IX_WrapPair_AB ON dbo.WrapPair(A, B);"
        + "CREATE TABLE dbo.WrapSmall (Id INT NOT NULL PRIMARY KEY CLUSTERED, V INT NOT NULL);"
        + "CREATE NONCLUSTERED INDEX IX_WrapSmall_V ON dbo.WrapSmall(V);"
        + "\nGO\nCREATE VIEW dbo.WrapView AS SELECT Id, Idx, NonKey, D FROM dbo.Wrap UNION ALL SELECT Id, Idx, NonKey, D FROM dbo.Wrap;";

    protected override string DatabaseNameSeed => nameof(NonSargableWrapSeekAvailabilityOracleTests);

    protected override string Ddl => StaticDdl.Replace(";", ";\nGO\n", StringComparison.Ordinal);

    protected override async Task SeedAsync()
    {
        await ExecuteAsync(
            """
            INSERT INTO dbo.Wrap (Id, Idx, NonKey, D)
            SELECT TOP (50000) n, n, n, DATEADD(DAY, n % 2000, '20200101')
            FROM (SELECT ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n FROM sys.all_objects a CROSS JOIN sys.all_objects b) x;

            INSERT INTO dbo.WrapPair (Id, A, B, Pad)
            SELECT n, n % 50, n, 'x' FROM (SELECT TOP (50000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n FROM sys.all_objects a CROSS JOIN sys.all_objects b) x;

            INSERT INTO dbo.WrapSmall (Id, V) VALUES (1, 10), (2, 20), (3, 30);

            UPDATE STATISTICS dbo.Wrap WITH FULLSCAN;
            UPDATE STATISTICS dbo.WrapPair WITH FULLSCAN;
            UPDATE STATISTICS dbo.WrapSmall WITH FULLSCAN;
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

    [Fact]
    public async Task WrappedIndexedColumnBesideFullUniqueKeyEquality_PlanStillSeeks_ScannerDoesNotReportIt()
    {
        const string Query = "SELECT Id FROM dbo.Wrap WHERE Id = 5 AND ABS(Idx) = 5;";

        Assert.True(await SeeksAsync(Query));
        Assert.Empty(Scan(Query));
    }

    [Fact]
    public async Task UniqueKeyEqualityInsideOrWithWrappedIndexedColumn_PlanDoesNotSeekTheWrappedColumn_ScannerStillReportsIt()
    {
        const string Query = "SELECT Id FROM dbo.Wrap WHERE Id = 5 OR ABS(Idx) = 5;";

        Assert.False(await SeeksAsync(Query));
        Assert.Single(Scan(Query), f => f.Confidence == FindingConfidence.High);
    }

    [Fact]
    public async Task WrappingNonLeadingKeyColumn_PlanIsUnchangedByTheWrap_ScannerDoesNotReportItAtHighConfidence()
    {
        const string Bare = "SELECT Id FROM dbo.WrapPair WHERE B = 5;";
        const string Wrapped = "SELECT Id FROM dbo.WrapPair WHERE ABS(B) = 5;";

        Assert.False(await SeeksAsync(Bare));
        Assert.False(await SeeksAsync(Wrapped));
        Assert.DoesNotContain(Scan(Wrapped), f => f.Confidence == FindingConfidence.High);
        Assert.Single(Scan("SELECT Id FROM dbo.WrapPair WHERE ABS(A) = 5;"), f => f.Confidence == FindingConfidence.High);
    }

    [Fact]
    public async Task WrappedIndexedColumnBesideSelectiveSeekOnAnotherIndexedColumn_PlanSeeksTheOtherColumn_ScannerDoesNotReportItAtHighConfidence()
    {
        const string Beside = "SELECT Id FROM dbo.Wrap WHERE Idx = 5 AND DATEPART(dw, D) = 3;";
        const string InsideOr = "SELECT Id FROM dbo.Wrap WHERE Idx = 5 OR DATEPART(dw, D) = 3;";

        Assert.True(await SeeksAsync(Beside));
        Assert.False(await SeeksAsync(InsideOr));
        Assert.DoesNotContain(Scan(Beside), f => f.ColumnName == "D" && f.Confidence == FindingConfidence.High);
        Assert.Contains(Scan(InsideOr), f => f.ColumnName == "D" && f.Confidence == FindingConfidence.High);
    }

    [Fact]
    public async Task WrappedOuterJoinColumnProbingABareIndexedKey_PlanSeeksTheBareKey_ScannerDoesNotReportItAtHighConfidence()
    {
        const string Probe = "SELECT w.Id FROM dbo.WrapSmall s JOIN dbo.Wrap w ON ABS(s.V) = w.Idx;";
        const string Variable = "SELECT s.Id FROM dbo.WrapSmall s WHERE ABS(s.V) = 10;";

        Assert.True(await SeeksAsync(Probe));
        Assert.False(await SeeksAsync(Variable));
        Assert.DoesNotContain(Scan(Probe), f => f.ColumnName == "V" && f.Confidence == FindingConfidence.High);
        Assert.Contains(Scan(Variable), f => f.ColumnName == "V" && f.Confidence == FindingConfidence.High);
    }

    [Fact]
    public async Task WrappedColumnComparedToAnotherColumnOfTheSameRow_BareFormDoesNotSeekEither_ScannerDoesNotReportIt()
    {
        const string Bare = "SELECT Id FROM dbo.Wrap WHERE Idx = NonKey;";
        const string Wrapped = "SELECT Id FROM dbo.Wrap WHERE ABS(Idx) = NonKey;";
        const string Constant = "SELECT Id FROM dbo.Wrap WHERE ABS(Idx) = 5;";

        Assert.False(await SeeksAsync(Bare));
        Assert.False(await SeeksAsync(Wrapped));
        Assert.Empty(Scan(Wrapped));
        Assert.Single(Scan(Constant), f => f.Confidence == FindingConfidence.High);
    }
}
