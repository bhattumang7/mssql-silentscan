using SilentScan.Core.Catalog;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;
using SilentScan.Core.TypeInference;

namespace SilentScan.Tests.Predicates;

public sealed class MissingStatisticsScannerTests
{
    private static CatalogTable Table(IReadOnlyList<CatalogStatisticsInfo>? statistics = null) =>
        new(
            "dbo", "Orders", CatalogTableKind.Table,
            [Col("OrderId"), Col("Region"), Col("Status")],
            Indexes: [],
            SourcePath: "dbo.Orders", SourceLine: 1,
            Statistics: statistics);

    private static CatalogColumn Col(string name) => new(name, new SqlType(SqlTypeCategory.Int), IsNullable: false, IsIdentity: false, IsComputed: false, IsPersisted: false);

    private static IReadOnlyList<MissingStatisticsFinding> Scan(string sql, DatabaseCatalog catalog)
    {
        var result = SqlScriptParser.ParseText("test.sql", sql);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        return MissingStatisticsScanner.Scan(result, catalog);
    }

    private static DatabaseCatalog Catalog(bool? isAutoCreateStatsOn, IReadOnlyList<CatalogStatisticsInfo>? statistics = null)
    {
        var catalog = new DatabaseCatalog { IsAutoCreateStatsOn = isAutoCreateStatsOn };
        catalog.AddOrReplace(Table(statistics));
        return catalog;
    }

    [Theory]
    [InlineData("CREATE TABLE #T (Id INT NOT NULL, V INT NULL); SELECT 1 FROM #T WHERE V = 5;")]
    [InlineData("CREATE TABLE ##T (Id INT NOT NULL, V INT NULL); SELECT 1 FROM ##T WHERE V = 5;")]
    [InlineData("DECLARE @t TABLE (Id INT NOT NULL, V INT NULL); SELECT 1 FROM @t WHERE V = 5;")]
    [InlineData("SELECT 1 AS Id, 2 AS V INTO #T; SELECT 1 FROM #T WHERE V = 5;")]
    public void AutoCreateStatsOff_PredicateOnATempTableOrTableVariable_NeverFires(string batch)
    {
        var parsed = SqlScriptParser.ParseText("test.sql", batch);
        var catalog = CatalogBuilder.Build([parsed]);
        catalog.IsAutoCreateStatsOn = false;

        Assert.Empty(MissingStatisticsScanner.Scan(parsed, catalog));
    }

    [Fact]
    public void AutoCreateStatsOff_PredicateOnAPermanentTableCreatedInTheSameBatch_StillFires()
    {
        var parsed = SqlScriptParser.ParseText("test.sql", "CREATE TABLE dbo.R (Id INT NOT NULL, V INT NULL); SELECT 1 FROM dbo.R WHERE V = 5;");
        var catalog = CatalogBuilder.Build([parsed]);
        catalog.IsAutoCreateStatsOn = false;

        Assert.Single(MissingStatisticsScanner.Scan(parsed, catalog));
    }

    [Fact]
    public void AutoCreateStatsUnknown_FileMode_NeverFires()
    {
        var findings = Scan("SELECT 1 FROM dbo.Orders WHERE Status = 5;", Catalog(isAutoCreateStatsOn: null));

        Assert.Empty(findings);
    }

    [Fact]
    public void AutoCreateStatsOn_NoStatisticsAtAll_StillClean()
    {
        var findings = Scan("SELECT 1 FROM dbo.Orders WHERE Status = 5;", Catalog(isAutoCreateStatsOn: true));

        Assert.Empty(findings);
    }

    [Fact]
    public void AutoCreateStatsOff_NoApplicableStatistic_Fires()
    {
        var findings = Scan("SELECT 1 FROM dbo.Orders WHERE Status = 5;", Catalog(isAutoCreateStatsOn: false));

        var finding = Assert.Single(findings);
        Assert.Equal("dbo.Orders", finding.TableQualifiedName);
        Assert.Equal("Status", finding.ColumnName);
    }

    [Fact]
    public void AutoCreateStatsOff_SingleColumnStatisticOnPredicateColumn_Clean()
    {
        var statistics = new[] { new CatalogStatisticsInfo("_WA_Status", NoRecompute: false, IsAutoCreated: true, ["Status"]) };
        var findings = Scan("SELECT 1 FROM dbo.Orders WHERE Status = 5;", Catalog(isAutoCreateStatsOn: false, statistics));

        Assert.Empty(findings);
    }

    [Fact]
    public void AutoCreateStatsOff_PredicateColumnIsLeadingKeyOfMultiColumnStatistic_Clean()
    {
        var statistics = new[] { new CatalogStatisticsInfo("IX_Region_Status", NoRecompute: false, IsAutoCreated: false, ["Region", "Status"]) };
        var findings = Scan("SELECT 1 FROM dbo.Orders WHERE Region = 5;", Catalog(isAutoCreateStatsOn: false, statistics));

        Assert.Empty(findings);
    }

    [Fact]
    public void AutoCreateStatsOff_PredicateColumnIsNonLeadingKeyOfMultiColumnStatistic_StillFires()
    {

        var statistics = new[] { new CatalogStatisticsInfo("IX_Region_Status", NoRecompute: false, IsAutoCreated: false, ["Region", "Status"]) };
        var findings = Scan("SELECT 1 FROM dbo.Orders WHERE Status = 5;", Catalog(isAutoCreateStatsOn: false, statistics));

        var finding = Assert.Single(findings);
        Assert.Equal("Status", finding.ColumnName);
    }

    [Fact]
    public void AutoCreateStatsOff_PredicateOnlyReachableThroughOr_Declines()
    {
        var findings = Scan("SELECT 1 FROM dbo.Orders WHERE Status = 5 OR OrderId = 1;", Catalog(isAutoCreateStatsOn: false));

        Assert.Empty(findings);
    }

    [Fact]
    public void AutoCreateStatsOff_TwoDifferentUncoveredColumns_ReportsBoth()
    {
        var findings = Scan("SELECT 1 FROM dbo.Orders WHERE Region = 1 AND Status = 5;", Catalog(isAutoCreateStatsOn: false));

        Assert.Equal(2, findings.Count);
        Assert.Contains(findings, f => f.ColumnName == "Region");
        Assert.Contains(findings, f => f.ColumnName == "Status");
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    public void ComputedColumn_IsReportedOnlyWhenKnownDeterministic(bool isDeterministic, int expected)
    {
        var catalog = new DatabaseCatalog { IsAutoCreateStatsOn = false };
        catalog.AddOrReplace(Table() with
        {
            Columns = [Col("OrderId") with { IsComputed = true, IsDeterministic = isDeterministic }],
        });

        Assert.Equal(expected, Scan("SELECT 1 FROM dbo.Orders WHERE OrderId = 1;", catalog).Count);
    }

    [Theory]
    [InlineData("SELECT 1 FROM dbo.Orders WHERE Status = 5;", true)]
    [InlineData("SELECT 1 FROM dbo.Orders WHERE Status = 5 AND Region = 1;", false)]
    public void AFilteredStatisticNeedsItsFilterColumnsInTheStatement(string statement, bool reportsStatus)
    {
        var catalog = Catalog(false, [
            new CatalogStatisticsInfo("S_Filtered", false, false, ["Status"], "([Region]=(1))"),
            new CatalogStatisticsInfo("S_Region", false, false, ["Region"]),
        ]);

        Assert.Equal(reportsStatus, Scan(statement, catalog).Any(f => f.ColumnName == "Status"));
    }
}
