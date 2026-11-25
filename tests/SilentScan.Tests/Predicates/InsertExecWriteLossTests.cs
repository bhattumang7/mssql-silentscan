using SilentScan.Core.Catalog;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;
using SilentScan.Core.Reporting;

namespace SilentScan.Tests.Predicates;

public sealed class InsertExecWriteLossTests
{
    private static ScanReport Scan(string callee, string target, string insert)
    {
        var sql = $"""
            CREATE TABLE dbo.Src (Amount DECIMAL(10,5) NOT NULL, Small DECIMAL(8,2) NOT NULL);
            CREATE TABLE dbo.Dst (Amount DECIMAL(10,2) NOT NULL);
            GO
            CREATE PROCEDURE dbo.usp_Other AS SELECT 1;
            GO
            {callee}
            GO
            CREATE PROCEDURE dbo.usp_Caller AS
            BEGIN
                {target}
                {insert}
            END
            """;
        var result = SqlScriptParser.ParseText("insertexec.sql", sql);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        return ScanReportBuilder.BuildFromParseResults([result], CatalogBuilder.Build([result]));
    }

    private const string SingleSelectCallee = "CREATE PROCEDURE dbo.usp_Get AS SELECT s.Amount FROM dbo.Src AS s;";

    [Theory]
    [InlineData("DECLARE @t TABLE (Amount DECIMAL(10,2) NOT NULL);", "INSERT INTO @t EXEC dbo.usp_Get;")]
    [InlineData("DECLARE @t TABLE (Amount DECIMAL(10,2) NOT NULL);", "INSERT INTO @t (Amount) EXEC dbo.usp_Get;")]
    [InlineData("", "INSERT INTO dbo.Dst EXEC dbo.usp_Get;")]
    [InlineData("", "INSERT dbo.Dst EXECUTE dbo.usp_Get;")]
    public void InsertExecOfASingleResultProcedure_FlagsTheNarrowedColumn(string target, string insert)
    {
        var report = Scan(SingleSelectCallee, target, insert);

        var finding = Assert.Single(report.Find<WriteLossFinding>("TypedPredicateExtractor"));
        Assert.Equal("Amount", finding.ColumnName);
        Assert.Equal(WriteLossKind.NumericScaleNarrowing, finding.Kind);
        Assert.DoesNotContain(report.SkippedConstructs, s => s.ConstructKind == "write source");
    }

    [Fact]
    public void InsertExecWhereTheResultColumnFits_FlagsNothing()
    {
        var report = Scan("CREATE PROCEDURE dbo.usp_Get AS SELECT s.Small FROM dbo.Src AS s;", string.Empty, "INSERT INTO dbo.Dst EXEC dbo.usp_Get;");

        Assert.Empty(report.Find<WriteLossFinding>("TypedPredicateExtractor"));
        Assert.DoesNotContain(report.SkippedConstructs, s => s.ConstructKind == "write source");
    }

    [Theory]
    [InlineData("CREATE PROCEDURE dbo.usp_Get AS BEGIN SELECT s.Amount FROM dbo.Src AS s; SELECT s.Amount FROM dbo.Src AS s; END")]
    [InlineData("CREATE PROCEDURE dbo.usp_Get @q NVARCHAR(100) AS BEGIN SELECT s.Amount FROM dbo.Src AS s; EXEC (@q); END")]
    [InlineData("CREATE PROCEDURE dbo.usp_Get AS BEGIN EXEC dbo.usp_Other; SELECT s.Amount FROM dbo.Src AS s; END")]
    [InlineData("CREATE PROCEDURE dbo.usp_Get AS BEGIN UPDATE dbo.Dst SET Amount = 1 OUTPUT inserted.Amount; SELECT s.Amount FROM dbo.Src AS s; END")]
    public void InsertExecOfAProcedureWithMoreThanOneResultProducer_StaysSkipped(string callee)
    {
        var report = Scan(callee, string.Empty, "INSERT INTO dbo.Dst EXEC dbo.usp_Get;");

        Assert.Empty(report.Find<WriteLossFinding>("TypedPredicateExtractor"));
        Assert.Contains(report.SkippedConstructs, s => s.ConstructKind == "write source");
    }

    [Fact]
    public void InsertExecIntoATempTable_StaysWithTheLiveShapeRule()
    {
        var report = Scan(SingleSelectCallee, "CREATE TABLE #t (Amount DECIMAL(10,2) NOT NULL);", "INSERT INTO #t EXEC dbo.usp_Get;");

        Assert.Empty(report.Find<WriteLossFinding>("TypedPredicateExtractor"));
        Assert.Contains(report.SkippedConstructs, s => s.ConstructKind == "write source");
    }
}
