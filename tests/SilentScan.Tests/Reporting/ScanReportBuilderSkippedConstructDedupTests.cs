using SilentScan.Core.Catalog;
using SilentScan.Core.Parsing;
using SilentScan.Core.Reporting;

namespace SilentScan.Tests.Reporting;

public sealed class ScanReportBuilderSkippedConstructDedupTests
{
    [Fact]
    public void SameConstructLedgeredByEveryPassThatResolvesIt_IsReportedOnce()
    {
        const string Sql = """
            CREATE TABLE dbo.Src (Id INT NOT NULL PRIMARY KEY);
            GO
            CREATE VIEW dbo.VOverUnknownTvf AS
            SELECT s.Id AS Id, u.x AS X FROM dbo.Src AS s CROSS APPLY dbo.NotRegistered(s.Id) AS u WHERE s.Id = 1;
            """;

        var result = SqlScriptParser.ParseText("dedup.sql", Sql);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));

        var report = ScanReportBuilder.BuildFromParseResults([result], CatalogBuilder.Build([result]));

        var tvfSkips = report.SkippedConstructs
            .Where(s => s.ConstructKind == "FROM table-valued function" && s.Reason.Contains("dbo.NotRegistered", StringComparison.Ordinal))
            .ToList();
        Assert.Single(tvfSkips);
        Assert.Equal(report.SkippedConstructs.Count, report.SkippedConstructs.Distinct().Count());
        Assert.Equal(report.SkippedConstructs.Count, report.SkippedConstructSummary.TotalCount);
    }
}
