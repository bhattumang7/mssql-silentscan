using SilentScan.Core.Catalog;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;
using SilentScan.Core.Reporting;

namespace SilentScan.Tests.Predicates;

public sealed class DynamicSqlScriptTempTableScopeTests
{
    private const string Base = """
        CREATE TABLE dbo.Source (Id INT NOT NULL, Code VARCHAR(10) NOT NULL, Note VARCHAR(10) NULL);
        GO
        """;

    private static ScanReport Scan(string procedure)
    {
        var result = SqlScriptParser.ParseText("scope.sql", Base + Environment.NewLine + procedure);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        return ScanReportBuilder.BuildFromParseResults([result], CatalogBuilder.Build([result]));
    }

    [Fact]
    public void SameTempNameWithDifferentShapesInSeparateScripts_EachScriptResolvesAgainstItsOwnDefinition()
    {
        var report = Scan("""
            CREATE PROCEDURE dbo.p AS
            BEGIN
                DECLARE @first varchar(max), @second varchar(max);
                SET @first = 'CREATE TABLE #T (A INT, OnlyInFirst VARCHAR(10)); SELECT x.OnlyInFirst FROM #T AS x WHERE x.OnlyInFirst = ''a'';';
                SET @second = 'CREATE TABLE #T (A INT, OnlyInSecond VARCHAR(10)); SELECT y.OnlyInSecond FROM #T AS y WHERE y.OnlyInSecond = ''a'';';
                EXEC (@first);
                EXEC (@second);
            END
            """);

        Assert.DoesNotContain(report.SkippedConstructs, s => s.ConstructKind == "column reference");
        Assert.DoesNotContain(report.SkippedConstructs, s => s.ConstructKind == "unresolved column comparison");
    }

    [Fact]
    public void SelectIntoInsideDynamicSql_TypesTheTempTableForLaterStatementsInTheSameScript()
    {
        var report = Scan("""
            CREATE PROCEDURE dbo.p AS
            BEGIN
                DECLARE @sql varchar(max);
                SET @sql = 'SELECT Id, Code INTO #Copy FROM dbo.Source; SELECT c.Code FROM #Copy AS c WHERE c.Code = ''a'';';
                EXEC (@sql);
            END
            """);

        Assert.DoesNotContain(report.SkippedConstructs, s => s.ConstructKind is "column reference" or "unresolved column comparison" or "FROM table reference");
    }
}
