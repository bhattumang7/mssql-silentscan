using SilentScan.Core.Catalog;
using SilentScan.Core.Parsing;
using SilentScan.Core.Reporting;

namespace SilentScan.Tests.Lineage;

public sealed class TableTypedVariableTests
{
    [Theory]
    [InlineData("DECLARE @ids dbo.IdList;")]
    [InlineData("DECLARE @ids IdList;")]
    [InlineData("DECLARE @other int, @ids dbo.IdList;")]
    public void VariableOfAUserTableType_ResolvesAsATableForWritesAndReads(string declaration)
    {
        var sql = $$"""
            CREATE TABLE dbo.Source (Id INT NOT NULL, Code VARCHAR(10) NOT NULL);
            GO
            CREATE TYPE dbo.IdList AS TABLE (Id INT NOT NULL);
            GO
            CREATE PROCEDURE dbo.p AS
            BEGIN
                {{declaration}}
                INSERT INTO @ids (Id) SELECT s.Id FROM dbo.Source AS s;
                SELECT s.Code FROM dbo.Source AS s JOIN @ids AS i ON i.Id = s.Id WHERE s.Code = 'x';
            END
            """;
        var result = SqlScriptParser.ParseText("tt.sql", sql);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));

        var report = ScanReportBuilder.BuildFromParseResults([result], CatalogBuilder.Build([result]));

        Assert.DoesNotContain(report.SkippedConstructs, s => s.ConstructKind is "FROM table reference" or "write target" or "column reference");
    }
}
