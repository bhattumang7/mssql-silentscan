using SilentScan.Core.Catalog;
using SilentScan.Core.Parsing;
using SilentScan.Core.Reporting;

namespace SilentScan.Tests.Lineage;

public sealed class UnknownRelationColumnSkipTests
{
    private static ScanReport Scan(string sql)
    {
        var result = SqlScriptParser.ParseText("unknown.sql", sql);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        return ScanReportBuilder.BuildFromParseResults([result], CatalogBuilder.Build([result]));
    }

    [Theory]
    [InlineData("SELECT 1 FROM #FromOutside AS t WHERE t.Amount = 1;")]
    [InlineData("SELECT 1 FROM #FromOutside WHERE Amount = 1;")]
    [InlineData("SELECT 1 FROM #FromOutside AS t WHERE t.Amount = 1 AND t.Label = 2 AND t.Code = 3;")]
    public void ColumnsOfAnUnknownRelation_AreOneSkipAtTheFromNotOneSkipPerColumn(string select)
    {
        var report = Scan($"CREATE PROCEDURE dbo.p AS {select}");

        var fromSkip = Assert.Single(report.SkippedConstructs, s => s.ConstructKind == "FROM table reference");
        Assert.Contains("#FromOutside", fromSkip.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain(report.SkippedConstructs, s => s.ConstructKind == "column reference");
    }

    [Fact]
    public void AMissingColumnOnAKnownTable_IsStillSkippedPerColumn()
    {
        var report = Scan("""
            CREATE TABLE dbo.Known (Amount INT NOT NULL);
            GO
            CREATE PROCEDURE dbo.p AS SELECT 1 FROM dbo.Known AS k WHERE k.Missing = 1;
            """);

        var skip = Assert.Single(report.SkippedConstructs, s => s.ConstructKind == "column reference" && s.Reason.Contains("Missing", StringComparison.Ordinal));
        Assert.Contains("Missing", skip.Reason, StringComparison.Ordinal);
    }
}
