using SilentScan.Core.Catalog;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;
using SilentScan.Core.Reporting;

namespace SilentScan.Tests.Reporting;

public sealed class ScanReportBuilderWrappedColumnOwnershipTests
{
    [Theory]
    [InlineData("COALESCE(t.EndDate, @d) >= @d")]
    [InlineData("@d <= ISNULL(t.EndDate, @d)")]
    [InlineData("CASE WHEN t.EndDate IS NULL THEN 1 ELSE 0 END = 1")]
    [InlineData("t.Qty + 1 = 5")]
    public void ComparisonOverAWrappedColumn_IsReportedBySargabilityAndNotAlsoSkipped(string predicate)
    {
        var result = SqlScriptParser.ParseText("wrapped.sql", $"""
            CREATE TABLE dbo.T (Id INT NOT NULL PRIMARY KEY, EndDate DATETIME NULL, Qty INT NOT NULL, INDEX IX_T_End (EndDate), INDEX IX_T_Qty (Qty));
            GO
            CREATE PROCEDURE dbo.p @d DATETIME AS SELECT t.Id FROM dbo.T AS t WHERE {predicate};
            """);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));

        var report = ScanReportBuilder.BuildFromParseResults([result], CatalogBuilder.Build([result]));

        Assert.NotEmpty(report.Find<SargabilityFinding>("NonSargablePredicateScanner"));
        Assert.DoesNotContain(report.SkippedConstructs, s => s.ConstructKind == "no column operand");
    }
}
