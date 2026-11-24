using SilentScan.Core.Catalog;
using SilentScan.Core.Parsing;
using SilentScan.Core.Reporting;

namespace SilentScan.Tests.Predicates;

public sealed class CurrentDatabaseNameFoldingTests
{
    private const string Sql = """
        CREATE TABLE dbo.Source (Id INT NOT NULL, Code VARCHAR(10) NOT NULL);
        GO
        CREATE PROCEDURE dbo.p AS
        BEGIN
            DECLARE @db varchar(100), @sql varchar(max);
            SELECT @db = DB_NAME();
            SET @sql = 'SELECT s.Code FROM ' + @db + '.dbo.Source AS s WHERE s.Code = ''a''';
            EXEC (@sql);
        END
        """;

    private static ScanReport Scan(string? currentDatabase)
    {
        var result = SqlScriptParser.ParseText("db.sql", Sql);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        var catalog = CatalogBuilder.Build([result]);
        catalog.CurrentDatabaseName = currentDatabase;
        return ScanReportBuilder.BuildFromParseResults([result], catalog);
    }

    [Fact]
    public void DbNameSplicedAsTheDatabaseQualifier_ResolvesToTheScannedDatabase()
    {
        var report = Scan("ScannedDb");

        Assert.DoesNotContain(report.SkippedConstructs, s => s.ConstructKind is "FROM table reference" or "column reference" or "unresolved column comparison");
    }

    [Fact]
    public void DbNameWithoutAKnownCurrentDatabase_StaysUnresolved()
    {
        var report = Scan(null);

        Assert.Contains(report.SkippedConstructs, s => s.ConstructKind == "FROM table reference");
    }
}
