using SilentScan.Core.Catalog;
using SilentScan.Core.Parsing;
using SilentScan.Core.Reporting;

namespace SilentScan.Tests.Lineage;

public sealed class ApplyCorrelationTests
{
    private const string Tables = """
        CREATE TABLE dbo.Outer1 (Id INT NOT NULL, Flag INT NOT NULL);
        CREATE TABLE dbo.Inner1 (Id INT NOT NULL, OuterId INT NOT NULL, Amount INT NOT NULL);
        GO
        """;

    private static ScanReport Scan(string body)
    {
        var result = SqlScriptParser.ParseText("apply.sql", Tables + Environment.NewLine + body);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        return ScanReportBuilder.BuildFromParseResults([result], CatalogBuilder.Build([result]));
    }

    private static void AssertNoLineageSkips(ScanReport report) =>
        Assert.False(
            report.SkippedConstructs.Any(s => s.ConstructKind is "column reference" or "unresolved column comparison" or "FROM table reference"),
            string.Join("; ", report.SkippedConstructs.Select(s => $"{s.ConstructKind}: {s.Reason}")));

    [Theory]
    [InlineData("CROSS APPLY")]
    [InlineData("OUTER APPLY")]
    public void ApplySubquerySelectListReferencingTheLeftSide_Resolves(string apply)
    {
        AssertNoLineageSkips(Scan($"""
            CREATE PROCEDURE dbo.p AS
            SELECT o.Id, a.Total
            FROM dbo.Outer1 AS o
            {apply} (SELECT CASE WHEN o.Flag = 0 THEN MIN(i.Amount) ELSE MAX(i.Amount) END AS Total FROM dbo.Inner1 AS i WHERE i.OuterId = o.Id) AS a
            WHERE o.Id = 1;
            """));
    }

    [Fact]
    public void ApplySubqueryReferencingACteJoinedOnTheLeft_Resolves()
    {
        AssertNoLineageSkips(Scan("""
            CREATE PROCEDURE dbo.p AS
            WITH settings AS (SELECT 1 AS Mode)
            SELECT o.Id, a.Total
            FROM dbo.Outer1 AS o
            CROSS JOIN settings
            CROSS APPLY (SELECT CASE WHEN Mode = 0 THEN MIN(i.Amount) ELSE MAX(i.Amount) END AS Total FROM dbo.Inner1 AS i WHERE i.OuterId = o.Id) AS a
            WHERE o.Id = 1;
            """));
    }
}
