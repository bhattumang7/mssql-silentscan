using SilentScan.Core.Catalog;
using SilentScan.Core.Parsing;
using SilentScan.Core.Reporting;

namespace SilentScan.Tests.Predicates;

public sealed class ExpressionDerivedViewColumnComparisonTests
{
    private static ScanReport Scan(string procedureBody)
    {
        var sql = $"""
            CREATE TABLE dbo.T1 (Id INT NOT NULL, Effective DATETIME NOT NULL);
            GO
            CREATE VIEW dbo.v AS SELECT t.Id, CAST(CONVERT(CHAR(8), t.Effective, 112) AS DATETIME) AS StartDt, CAST(CONVERT(CHAR(8), t.Effective, 112) AS DATETIME) AS EndDt FROM dbo.T1 AS t;
            GO
            CREATE PROCEDURE dbo.p @d DATETIME AS
            {procedureBody}
            """;
        var result = SqlScriptParser.ParseText("expr.sql", sql);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        return ScanReportBuilder.BuildFromParseResults([result], CatalogBuilder.Build([result]));
    }

    [Fact]
    public void BetweenBoundsThatAreResolvedExpressionDerivedViewColumns_AreNotLedgeredAsUnresolved()
    {
        var report = Scan("SELECT x.Id FROM dbo.v AS x WHERE COALESCE(@d, GETDATE()) BETWEEN x.StartDt AND x.EndDt;");

        Assert.DoesNotContain(report.SkippedConstructs, s => s.ConstructKind == "unresolved column comparison");
    }

    [Fact]
    public void ComparisonAgainstAColumnThatDoesNotExist_IsStillLedgeredAsUnresolved()
    {
        var report = Scan("SELECT x.Id FROM dbo.v AS x WHERE COALESCE(@d, GETDATE()) BETWEEN x.Missing AND x.EndDt;");

        Assert.Contains(report.SkippedConstructs, s => s.ConstructKind == "unresolved column comparison" && s.Reason.Contains("x.Missing", StringComparison.Ordinal));
    }
}
