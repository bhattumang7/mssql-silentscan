using SilentScan.Core.Catalog;
using SilentScan.Core.Parsing;
using SilentScan.Core.Reporting;

namespace SilentScan.Tests.Lineage;

public sealed class CrossModuleAndSelfQualifiedResolutionTests
{
    private static ScanReport Scan(string sql, string? currentDatabase = null)
    {
        var result = SqlScriptParser.ParseText("resolution.sql", sql);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        var catalog = CatalogBuilder.Build([result]);
        catalog.CurrentDatabaseName = currentDatabase;
        return ScanReportBuilder.BuildFromParseResults([result], catalog);
    }

    [Theory]
    [InlineData("ScannedDb.dbo.vSource")]
    [InlineData("ScannedDb.dbo.Source")]
    public void ThreePartNameInTheScannedDatabase_ResolvesLikeTheTwoPartName(string name)
    {
        var report = Scan($"""
            CREATE TABLE dbo.Source (Id INT NOT NULL, Code VARCHAR(10) NOT NULL);
            GO
            CREATE VIEW dbo.vSource AS SELECT Id, Code FROM dbo.Source;
            GO
            CREATE PROCEDURE dbo.p AS SELECT s.Code FROM {name} AS s WHERE s.Code = 'a';
            """, "ScannedDb");

        Assert.DoesNotContain(report.SkippedConstructs, s => s.ConstructKind is "FROM table reference" or "column reference" or "unresolved column comparison");
    }

    [Theory]
    [InlineData("EXEC dbo.callee;")]
    [InlineData("EXEC callee;")]
    [InlineData("EXEC callee @a, @b;")]
    [InlineData("EXEC callee @a, @b OUTPUT;")]
    public void TempTableCreatedByTheCallingProcedure_ResolvesInsideTheCalledProcedure(string call)
    {
        var report = Scan($"""
            CREATE PROCEDURE dbo.callee (@a INT = 0, @b INT = 0 OUTPUT) AS SELECT t.Code FROM #shared AS t WHERE t.Code = 'a';
            GO
            CREATE PROCEDURE dbo.caller AS
            BEGIN
                DECLARE @a INT, @b INT;
                CREATE TABLE #shared (Id INT NOT NULL, Code VARCHAR(10) NOT NULL);
                {call}
            END
            """);

        Assert.DoesNotContain(report.SkippedConstructs, s => s.ConstructKind is "FROM table reference" or "column reference" or "unresolved column comparison");
    }

    [Fact]
    public void TempTableCreatedTwoCallersUp_ResolvesInsideTheDeepestCalledProcedure()
    {
        var report = Scan("""
            CREATE PROCEDURE dbo.deepest AS SELECT t.Code FROM #shared AS t WHERE t.Code = 'a';
            GO
            CREATE PROCEDURE dbo.middle AS EXEC dbo.deepest;
            GO
            CREATE PROCEDURE dbo.top1 AS
            BEGIN
                CREATE TABLE #shared (Id INT NOT NULL, Code VARCHAR(10) NOT NULL);
                EXEC dbo.middle;
            END
            """);

        Assert.DoesNotContain(report.SkippedConstructs, s => s.ConstructKind is "FROM table reference" or "column reference" or "unresolved column comparison");
    }

    [Fact]
    public void TempTableInAScalarSubqueryOfACalledProcedure_ResolvesThroughTheCaller()
    {
        var report = Scan("""
            CREATE PROCEDURE dbo.callee AS
            BEGIN
                DECLARE @v INT;
                SET @v = (SELECT TOP 1 s.Id FROM #shared AS s WHERE s.Code = 'a');
            END
            GO
            CREATE PROCEDURE dbo.caller AS
            BEGIN
                CREATE TABLE #shared (Id INT NOT NULL, Code VARCHAR(10) NOT NULL);
                EXEC dbo.callee;
            END
            """);

        Assert.DoesNotContain(report.SkippedConstructs, s => s.ConstructKind is "FROM table reference" or "column reference" or "unresolved column comparison");
    }
}
