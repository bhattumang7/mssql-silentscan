using SilentScan.Core.Catalog;
using SilentScan.Core.Parsing;
using SilentScan.Core.Reporting;

namespace SilentScan.Tests.Lineage;

public sealed class SelectIntoStarChainTests
{
    [Fact]
    public void SelectIntoThatMixesAliasedExpressionsWithAStarOverAnotherSelectIntoTable_ExposesTheStarColumns()
    {
        var sql = """
            CREATE TABLE dbo.Source (Id INT NOT NULL, Code VARCHAR(10) NOT NULL);
            GO
            CREATE VIEW dbo.vSource AS SELECT Id, Code FROM dbo.Source;
            GO
            CREATE PROCEDURE dbo.p AS
            BEGIN
                SELECT * INTO #a FROM dbo.vSource;
                SELECT a.Id * 2 AS Doubled, a.* INTO #b FROM #a AS a;
                SELECT b.Code, b.Doubled FROM #b AS b WHERE b.Code = 'x';
            END
            """;
        var result = SqlScriptParser.ParseText("chain.sql", sql);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));

        var report = ScanReportBuilder.BuildFromParseResults([result], CatalogBuilder.Build([result]));

        Assert.DoesNotContain(report.SkippedConstructs, s => s.ConstructKind is "column reference" or "unresolved column comparison");
    }
}
