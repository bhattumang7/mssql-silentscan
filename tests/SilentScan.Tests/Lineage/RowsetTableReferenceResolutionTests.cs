using SilentScan.Core.Catalog;
using SilentScan.Core.Parsing;
using SilentScan.Core.Reporting;

namespace SilentScan.Tests.Lineage;

public sealed class RowsetTableReferenceResolutionTests
{
    private static ScanReport Scan(string sql, string? currentDatabase = null)
    {
        var result = SqlScriptParser.ParseText("rowset.sql", sql);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        var catalog = CatalogBuilder.Build([result]);
        catalog.CurrentDatabaseName = currentDatabase;
        return ScanReportBuilder.BuildFromParseResults([result], catalog);
    }

    private static void AssertNothingSkipped(ScanReport report) =>
        Assert.False(
            report.SkippedConstructs.Any(s => s.ConstructKind is "FROM table reference" or "column reference" or "unresolved column comparison" or "FROM table-valued function"),
            string.Join("; ", report.SkippedConstructs.Select(s => $"{s.ConstructKind}: {s.Reason}")));

    [Fact]
    public void OpenXmlWithColumnDeclarations_ExposesTheDeclaredColumns()
    {
        var report = Scan("""
            CREATE PROCEDURE dbo.p @doc INT AS
            SELECT x.Amount, x.Label
            FROM OPENXML (@doc, '/Rows/Row') WITH (Amount INT, Label VARCHAR(20)) AS x
            WHERE x.Amount = 3;
            """);

        AssertNothingSkipped(report);
    }

    [Fact]
    public void OpenXmlWithAnExistingTableAsItsSchema_ExposesThatTablesColumns()
    {
        var report = Scan("""
            CREATE TABLE dbo.Shape (Amount INT NOT NULL, Label VARCHAR(20) NULL);
            GO
            CREATE PROCEDURE dbo.p @doc INT AS
            SELECT x.Amount FROM OPENXML (@doc, '/Rows/Row') WITH dbo.Shape AS x WHERE x.Amount = 3;
            """);

        AssertNothingSkipped(report);
    }

    [Fact]
    public void ValuesDerivedTableWithColumnAliases_ExposesThoseColumns()
    {
        var report = Scan("""
            CREATE PROCEDURE dbo.p AS
            SELECT v.A FROM (VALUES (1, 'x'), (2, 'y')) AS v (A, B) WHERE v.A = 1 AND v.B = 'x';
            """);

        AssertNothingSkipped(report);
    }

    [Fact]
    public void TableValuedFunctionNamedWithTheScannedDatabase_ResolvesLikeTheTwoPartName()
    {
        var report = Scan("""
            CREATE TABLE dbo.Source (Id INT NOT NULL, Code VARCHAR(10) NOT NULL);
            GO
            CREATE FUNCTION dbo.fnRows () RETURNS TABLE AS RETURN (SELECT Id, Code FROM dbo.Source);
            GO
            CREATE PROCEDURE dbo.p AS SELECT f.Code FROM ScannedDb.dbo.fnRows() AS f WHERE f.Id = 1;
            """, "ScannedDb");

        AssertNothingSkipped(report);
    }
}
