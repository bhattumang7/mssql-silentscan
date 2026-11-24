using SilentScan.Core.Catalog;
using SilentScan.Core.Parsing;

namespace SilentScan.Tests.Lineage;

public sealed class CteColumnListTests
{
    [Theory]
    [InlineData("WITH c (Id, Flag) AS (SELECT t.A, t.B FROM dbo.T1 AS t UNION ALL SELECT CAST(NULL AS INT), CAST(0 AS BIT)) SELECT Id, Flag FROM c")]
    [InlineData("WITH c (Id, Flag) AS (SELECT t.A, t.B FROM dbo.T1 AS t) SELECT Id, Flag FROM c")]
    [InlineData("WITH c AS (SELECT t.A AS Id, t.B AS Flag FROM dbo.T1 AS t) SELECT Id, Flag FROM c")]
    [InlineData("SELECT t.A AS Id, t.B AS Flag FROM dbo.T1 AS t UNION ALL SELECT CAST(NULL AS INT), CAST(0 AS BIT)")]
    [InlineData("SELECT t.A AS Id, t.B AS Flag FROM dbo.T1 AS t")]
    [InlineData("SELECT t.A AS Id, t.B AS Flag FROM dbo.T1 AS t UNION ALL SELECT NULL, NULL")]
    [InlineData("SELECT NULL AS Id, NULL AS Flag UNION ALL SELECT t.A, t.B FROM dbo.T1 AS t")]
    public void ViewOverCteWithColumnListOverUnion_ExposesTheListedNames(string viewBody)
    {
        var sql = $"""
            CREATE TABLE dbo.T1 (A INT NOT NULL, B BIT NOT NULL);
            GO
            CREATE VIEW dbo.v AS
            {viewBody};
            GO
            CREATE PROCEDURE dbo.p AS SELECT x.Id FROM dbo.v AS x WHERE x.Flag = 0 AND x.Id = 3;
            """;
        var result = SqlScriptParser.ParseText("cte.sql", sql);
        Assert.False(result.HasErrors);
        var report = SilentScan.Core.Reporting.ScanReportBuilder.BuildFromParseResults([result], CatalogBuilder.Build([result]));
        Assert.True(report.SkippedConstructs.Count == 0, string.Join("; ", report.SkippedConstructs.Select(s => $"{s.ConstructKind}: {s.Reason}")));
    }
}
