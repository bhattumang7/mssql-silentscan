using SilentScan.Core.Catalog;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;
using SilentScan.Verify;
using SilentScan.Verify.Deployment;
using SilentScan.Verify.Oracle;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
[Trait("Rule", "silentscan/temp-table/unindexed-join-operand")]
public sealed class UnindexedTempTableCrossJoinOracleTests : IAsyncLifetime
{
    private const string DatabaseName = "SilentScanTempCrossJoinOracleTest";

    private readonly SqlServerOptions _options = SqlServerOptions.LocalDocker;
    private readonly DatabaseProvisioner _provisioner;

    public UnindexedTempTableCrossJoinOracleTests()
    {
        _provisioner = new DatabaseProvisioner(_options);
    }

    public async Task InitializeAsync() => await _provisioner.CreateFreshAsync(DatabaseName);

    public async Task DisposeAsync() => await _provisioner.DropIfExistsAsync(DatabaseName);

    private static readonly IReadOnlyList<string> PopulateAndIndexTempTable =
    [
        "CREATE TABLE dbo.Widgets (WidgetId INT NOT NULL PRIMARY KEY, Code VARCHAR(20) NOT NULL);",
        "INSERT INTO dbo.Widgets (WidgetId, Code) VALUES (1, 'A'), (2, 'B');",
        "SELECT WidgetId AS Id, Code INTO #t FROM dbo.Widgets;",
        "CREATE INDEX IX_t_Code ON #t (Code);",
    ];

    [Fact]
    public async Task CrossJoinedTempTable_NeverSeeksEvenWhenIndexed()
    {
        var planXml = await new PlanXmlCapture(_options).CaptureAsync(
            DatabaseName, "SELECT w.WidgetId FROM dbo.Widgets AS w CROSS JOIN #t;", PopulateAndIndexTempTable);

        Assert.False(IndexAccessDetector.HasIndexSeek(planXml, "IX_t_Code"));
    }

    [Fact]
    public async Task InnerJoinedTempTable_SeeksWhenIndexed()
    {
        var planXml = await new PlanXmlCapture(_options).CaptureAsync(
            DatabaseName, "SELECT w.WidgetId FROM dbo.Widgets AS w INNER JOIN #t AS t ON w.Code = t.Code;", PopulateAndIndexTempTable);

        Assert.True(IndexAccessDetector.HasIndexSeek(planXml, "IX_t_Code"));
    }

    [Fact]
    public async Task CrossJoinedTempTableWithCorrelatingWherePredicate_CanSeekWhenIndexed()
    {
        var planXml = await new PlanXmlCapture(_options).CaptureAsync(
            DatabaseName,
            "SELECT w.WidgetId FROM dbo.Widgets AS w CROSS JOIN #t WHERE w.Code = #t.Code OPTION (LOOP JOIN, FORCE ORDER);",
            PopulateAndIndexTempTable);

        Assert.True(IndexAccessDetector.HasIndexSeek(planXml, "IX_t_Code"));
    }

    [Fact]
    public void Scanner_DoesNotFireJoinOperand_ForCrossJoinedTempTable_WithNoCorrelatingPredicate()
    {
        const string sql = """
            CREATE TABLE dbo.Widgets (WidgetId INT NOT NULL PRIMARY KEY, Code VARCHAR(20) NOT NULL);
            SELECT WidgetId AS Id, Code INTO #t FROM dbo.Widgets;
            SELECT w.WidgetId FROM dbo.Widgets AS w CROSS JOIN #t;
            """;

        var result = SqlScriptParser.ParseText("test.sql", sql);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));

        var catalog = CatalogBuilder.Build([result]);
        var findings = UnindexedTempTableUsageScanner.Scan(result, catalog);

        Assert.DoesNotContain(findings, f => f.Kind == UnindexedTempTableUsageKind.JoinOperand);
    }

    [Fact]
    public void Scanner_FiresJoinOperand_ForCrossJoinedTempTable_WithCorrelatingWherePredicate()
    {
        const string sql = """
            CREATE TABLE dbo.Widgets (WidgetId INT NOT NULL PRIMARY KEY, Code VARCHAR(20) NOT NULL);
            SELECT WidgetId AS Id, Code INTO #t FROM dbo.Widgets;
            SELECT w.WidgetId FROM dbo.Widgets AS w CROSS JOIN #t WHERE w.Code = #t.Code;
            """;

        var result = SqlScriptParser.ParseText("test.sql", sql);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));

        var catalog = CatalogBuilder.Build([result]);
        var findings = UnindexedTempTableUsageScanner.Scan(result, catalog);

        Assert.Contains(findings, f => f.Kind == UnindexedTempTableUsageKind.JoinOperand);
    }
}
