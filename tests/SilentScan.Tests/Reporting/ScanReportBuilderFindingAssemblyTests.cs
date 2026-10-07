using SilentScan.Core.Catalog;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;
using SilentScan.Core.Reporting;
using SilentScan.Core.Rules;

namespace SilentScan.Tests.Reporting;

public sealed class ScanReportBuilderFindingAssemblyTests
{
    [Fact]
    public void EveryFinding_IsReportedWithItsOwnConfidence()
    {
        const string Sql = """
            CREATE TABLE dbo.T (Id INT NOT NULL PRIMARY KEY, Col INT NOT NULL);
            GO
            CREATE VIEW dbo.VHundredPercent AS SELECT TOP (100) PERCENT Id, Col FROM dbo.T ORDER BY Col;
            GO
            CREATE VIEW dbo.VTopRows AS SELECT TOP (5) Id, Col FROM dbo.T ORDER BY Col;
            """;

        var result = SqlScriptParser.ParseText("confidence.sql", Sql);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        var catalog = CatalogBuilder.Build([result]);

        var report = ScanReportBuilder.BuildFromParseResults([result], catalog);

        var findings = report.Find<ViewOrderingFinding>("ViewOrderingScanner");
        Assert.Equal(2, findings.Count);
        Assert.Contains(findings, f => f.Kind == ViewOrderingFindingKind.TopPercentOrderByNeverLimits && f.Confidence == FindingConfidence.High);
        Assert.Contains(findings, f => f.Kind == ViewOrderingFindingKind.OrderByNotGuaranteedToConsumer && f.Confidence == FindingConfidence.Low);
    }

    [Fact]
    public void SeekPreservedAndUnknownVerdicts_AreExcludedFromTypedFindingsButStillCountedInSummary()
    {
        const string Sql = """
            CREATE TABLE dbo.OrdersUnknownCol (Col INT NOT NULL);
            GO
            DECLARE @LeakedVar INT = 1; SELECT 1;
            GO
            SELECT 1 FROM dbo.OrdersUnknownCol WHERE Col = @LeakedVar;
            GO
            CREATE TABLE dbo.OrdersVariantCol (OrderId INT NOT NULL PRIMARY KEY, Tag SQL_VARIANT NOT NULL, INDEX IX_OrdersVariantCol_Tag (Tag));
            GO
            SELECT 1 FROM dbo.OrdersVariantCol WHERE Tag = 5;
            GO
            CREATE TABLE dbo.OrdersIntCol (OrderId INT NOT NULL PRIMARY KEY, Quantity INT NOT NULL, INDEX IX_OrdersIntCol_Quantity (Quantity));
            GO
            SELECT 1 FROM dbo.OrdersIntCol WHERE Quantity = CAST(5 AS SQL_VARIANT);
            """;

        var result = SqlScriptParser.ParseText("verdicts.sql", Sql);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        var catalog = CatalogBuilder.Build([result]);

        var report = ScanReportBuilder.BuildFromParseResults([result], catalog);

        Assert.DoesNotContain(report.Find<TypedPredicateFinding>("TypedPredicateExtractor"), f => f.Verdict == Verdict.SeekPreserved);
        Assert.Equal(1, report.TypedPredicateSummary.SeekPreservedCount);

        var typedFindings = report.Find<TypedPredicateFinding>("TypedPredicateExtractor").ToList();

        Assert.DoesNotContain(typedFindings, f => f.Verdict == Verdict.Unknown);
        Assert.Equal(1, report.TypedPredicateSummary.UnknownCount);
        Assert.Contains(typedFindings, f => f.Verdict == Verdict.ScanForced);
        Assert.All(typedFindings, f => Assert.NotNull(f.RuleId));
    }

    [Fact]
    public void UnanalyzableDynamicSql_IsACoverageRecord_NeverAFindingWithoutARuleId()
    {
        const string Sql = """
            CREATE TABLE dbo.T (Col INT NOT NULL);
            GO
            CREATE PROCEDURE dbo.usp_Run @Sql NVARCHAR(MAX) AS
            BEGIN
                EXEC(@Sql);
            END
            """;
        var parseResult = SqlScriptParser.ParseText("dyn.sql", Sql);
        Assert.False(parseResult.HasErrors);

        var report = ScanReportBuilder.BuildFromParseResults([parseResult], CatalogBuilder.Build([parseResult]));

        var withoutRuleId = report.FindingsByRuleId.Values.SelectMany(findings => findings)
            .Where(f => f.GetType().GetProperty("RuleId")?.GetValue(f) is not string)
            .ToList();
        Assert.Empty(withoutRuleId);
        Assert.Equal(1, report.DynamicSqlSummary.UnanalyzableCount);
        var coverage = Assert.Single(report.DynamicSqlCallSites);
        Assert.Equal(DynamicSqlOutcome.Unanalyzable, coverage.Outcome);
    }

    [Fact]
    public void EmptyParseResults_ProduceAnEmptyReportAcrossEveryStreamWithoutThrowing()
    {
        var report = ScanReportBuilder.BuildFromParseResults([], new DatabaseCatalog());

        Assert.Empty(report.ParseHealth.Files);
        Assert.Empty(report.Find<SargabilityFinding>("NonSargablePredicateScanner"));
        Assert.Empty(report.Find<TypedPredicateFinding>("TypedPredicateExtractor"));
        Assert.Empty(report.DynamicSqlCallSites);
        Assert.Empty(report.SkippedConstructs);
        Assert.Equal(0, report.TypedPredicateSummary.TotalClassified);
        Assert.Equal(0, report.DynamicSqlSummary.TotalCallSites);
        Assert.Equal(0, report.SkippedConstructSummary.TotalCount);
    }
}
