using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;
using SilentScan.Verify.Catalog;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.MissingStatisticsRuleId)]
public sealed class MissingStatisticsAutoCreateDisabledOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(MissingStatisticsAutoCreateDisabledOracleTests);

    protected override string Ddl => """
        CREATE TABLE dbo.T1 (A INT NOT NULL, B INT NOT NULL, C INT NOT NULL);
        GO
        CREATE INDEX IX_T1_AB ON dbo.T1 (A, B);
        GO
        ALTER DATABASE CURRENT SET AUTO_CREATE_STATISTICS OFF;
        GO
        """;

    [Fact]
    public async Task LeadingKeyColumn_Clean_NonLeadingAndUncoveredColumns_Fire()
    {
        var catalog = await new LiveCatalogReader(Options.BuildConnectionString(DatabaseName)).ReadAsync();
        Assert.False(catalog.IsAutoCreateStatsOn);

        var parseResult = SqlScriptParser.ParseText(
            "test.sql",
            "SELECT 1 FROM dbo.T1 WHERE A = 1 AND B = 2 AND C = 3;");
        Assert.False(parseResult.HasErrors);

        var findings = MissingStatisticsScanner.Scan(parseResult, catalog);

        var flaggedColumns = findings.Select(f => f.ColumnName).ToHashSet();
        Assert.DoesNotContain("A", flaggedColumns);
        Assert.Contains("B", flaggedColumns);
        Assert.Contains("C", flaggedColumns);
    }

    [Fact]
    public async Task TempTablePredicate_TheEngineStillAutoCreatesItsStatistic_SoTheScannerStaysSilent()
    {
        var connectionString = Options.BuildConnectionString(DatabaseName);
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new Microsoft.Data.SqlClient.SqlCommand(
            """
            CREATE TABLE #T (Id INT NOT NULL, V INT NULL);
            INSERT #T VALUES (1, 1), (2, 2);
            SELECT COUNT(*) FROM #T WHERE V = 1 OPTION (RECOMPILE);
            SELECT COUNT(*) FROM tempdb.sys.stats s
            JOIN tempdb.sys.stats_columns sc ON sc.object_id = s.object_id AND sc.stats_id = s.stats_id
            JOIN tempdb.sys.columns c ON c.object_id = sc.object_id AND c.column_id = sc.column_id
            WHERE s.object_id = OBJECT_ID('tempdb..#T') AND c.name = 'V';
            """,
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.NextResultAsync();
        await reader.ReadAsync();
        Assert.Equal(1, reader.GetInt32(0));

        var catalog = await new LiveCatalogReader(connectionString).ReadAsync();
        Assert.False(catalog.IsAutoCreateStatsOn);
        var parseResult = SqlScriptParser.ParseText(
            "test.sql",
            "CREATE TABLE #T (Id INT NOT NULL, V INT NULL); SELECT 1 FROM #T WHERE V = 1;");
        catalog.MergeFileModeExtras(SilentScan.Core.Catalog.CatalogBuilder.Build([parseResult]));

        Assert.Empty(MissingStatisticsScanner.Scan(parseResult, catalog));
    }
}

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.MissingStatisticsRuleId)]
public sealed class MissingStatisticsAutoCreateEnabledOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(MissingStatisticsAutoCreateEnabledOracleTests);

    protected override string Ddl => """
        CREATE TABLE dbo.T1 (A INT NOT NULL, B INT NOT NULL);
        GO
        """;

    [Fact]
    public async Task AutoCreateStatisticsOn_NeverFires()
    {
        var catalog = await new LiveCatalogReader(Options.BuildConnectionString(DatabaseName)).ReadAsync();
        Assert.True(catalog.IsAutoCreateStatsOn);

        var parseResult = SqlScriptParser.ParseText(
            "test.sql",
            "SELECT 1 FROM dbo.T1 WHERE A = 1 AND B = 2;");
        Assert.False(parseResult.HasErrors);

        var findings = MissingStatisticsScanner.Scan(parseResult, catalog);

        Assert.Empty(findings);
    }
}

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.MissingStatisticsRuleId)]
public sealed class MissingStatisticsSingleColumnStatisticOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(MissingStatisticsSingleColumnStatisticOracleTests);

    protected override string Ddl => """
        CREATE TABLE dbo.T1 (A INT NOT NULL, B INT NOT NULL);
        GO
        CREATE STATISTICS ST_T1_B ON dbo.T1 (B);
        GO
        ALTER DATABASE CURRENT SET AUTO_CREATE_STATISTICS OFF;
        GO
        """;

    [Fact]
    public async Task ExplicitSingleColumnStatistic_Clean()
    {
        var catalog = await new LiveCatalogReader(Options.BuildConnectionString(DatabaseName)).ReadAsync();

        var parseResult = SqlScriptParser.ParseText("test.sql", "SELECT 1 FROM dbo.T1 WHERE B = 2;");
        Assert.False(parseResult.HasErrors);

        var findings = MissingStatisticsScanner.Scan(parseResult, catalog);

        Assert.Empty(findings);
    }
}
