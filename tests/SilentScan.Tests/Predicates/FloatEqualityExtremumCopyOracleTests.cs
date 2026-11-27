using SilentScan.Core.Catalog;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.FloatEqualityRuleId)]
public sealed class FloatEqualityExtremumCopyOracleTests : OracleTestFixture
{
    private const string TableDdl = "CREATE TABLE dbo.Readings (Grp INT NOT NULL, V FLOAT NOT NULL);";

    protected override string DatabaseNameSeed => nameof(FloatEqualityExtremumCopyOracleTests);

    protected override string Ddl => TableDdl;

    protected override async Task SeedAsync()
    {
        await ExecuteAsync("INSERT dbo.Readings (Grp, V) VALUES (1, 0.1), (1, 0.2), (1, 0.3);");
    }

    [Fact]
    public async Task JoinBackToWindowMinOfTheSameColumn_ValueIsAnExactCopy_EngineMatches_ScannerDoesNotFlag()
    {
        const string batch = """
            DECLARE @m TABLE (Grp INT, MinV FLOAT);
            INSERT INTO @m SELECT DISTINCT Grp, MIN(V) OVER (PARTITION BY Grp) FROM dbo.Readings;
            SELECT COUNT(*) FROM dbo.Readings r JOIN @m m ON m.MinV = r.V AND m.Grp = r.Grp;
            """;

        Assert.Equal(1, await ScalarAsync<int>(batch));

        Assert.Empty(Scan(batch));
    }

    [Fact]
    public async Task JoinBackToWindowMinOfTheSameColumn_ThroughTempTableAndGroupBy_ScannerDoesNotFlag()
    {
        const string batch = """
            CREATE TABLE #m (Grp INT NOT NULL, MinV FLOAT NOT NULL);
            INSERT INTO #m (Grp, MinV) SELECT Grp, MIN(V) FROM dbo.Readings GROUP BY Grp;
            SELECT COUNT(*) FROM dbo.Readings r JOIN #m m ON m.MinV = r.V AND m.Grp = r.Grp;
            """;

        Assert.Equal(1, await ScalarAsync<int>(batch));

        Assert.Empty(Scan(batch));
    }

    [Fact]
    public async Task JoinBackToWindowAverageOfTheColumn_ValueIsComputed_EngineMissesTheRow_ScannerFlags()
    {
        const string batch = """
            DECLARE @a TABLE (Grp INT, AvgV FLOAT);
            INSERT INTO @a SELECT DISTINCT Grp, AVG(V) OVER (PARTITION BY Grp) FROM dbo.Readings;
            SELECT COUNT(*) FROM dbo.Readings r JOIN @a a ON a.AvgV = r.V AND a.Grp = r.Grp;
            """;

        Assert.Equal(0, await ScalarAsync<int>(batch));

        var finding = Assert.Single(Scan(batch));
        Assert.Equal("AvgV", finding.ColumnName);
    }

    [Fact]
    public async Task JoinBackToWindowMinThatIsLaterUpdated_ValueIsNoLongerACopy_EngineMissesTheRow_ScannerFlags()
    {
        const string batch = """
            DECLARE @m TABLE (Grp INT, MinV FLOAT);
            INSERT INTO @m SELECT DISTINCT Grp, MIN(V) OVER (PARTITION BY Grp) FROM dbo.Readings;
            UPDATE @m SET MinV = MinV + 0.000001;
            SELECT COUNT(*) FROM dbo.Readings r JOIN @m m ON m.MinV = r.V AND m.Grp = r.Grp;
            """;

        Assert.Equal(0, await ScalarAsync<int>(batch));

        Assert.NotEmpty(Scan(batch));
    }

    private static IReadOnlyList<FloatEqualityFinding> Scan(string batch)
    {
        var parsed = SqlScriptParser.ParseText("test.sql", $"{TableDdl}\nGO\nCREATE PROCEDURE dbo.Probe AS\nBEGIN\n{batch}\nEND");
        Assert.False(parsed.HasErrors, string.Join("; ", parsed.Errors.Select(e => e.Message)));

        var catalog = CatalogBuilder.Build([parsed]);
        return FloatEqualityPredicateScanner.Scan(parsed, catalog);
    }
}
