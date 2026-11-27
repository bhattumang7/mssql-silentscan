using SilentScan.Core.Rules;
using System.Xml.Linq;
using SilentScan.Core.Catalog;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.ScalarUdfProjectionInvocationRuleId)]
public sealed class ScalarUdfProjectionRowSourceOracleTests : OracleTestFixture
{
    private const int RowCount = 50;

    protected override string DatabaseNameSeed => nameof(ScalarUdfProjectionRowSourceOracleTests);

    protected override string Ddl => $"""
        CREATE TABLE dbo.Source (Id INT NOT NULL PRIMARY KEY, V INT NOT NULL DEFAULT 0);
        GO
        INSERT INTO dbo.Source (Id) SELECT TOP ({RowCount}) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) FROM sys.all_objects;
        GO
        CREATE FUNCTION dbo.Bump(@x INT) RETURNS INT WITH INLINE = OFF AS BEGIN RETURN @x + 1; END;
        GO
        CREATE FUNCTION dbo.Wrap(@x INT) RETURNS TABLE AS RETURN SELECT @x AS V;
        GO
        CREATE TABLE dbo.Keyed (Code VARCHAR(20) NOT NULL PRIMARY KEY, V INT NOT NULL DEFAULT 0);
        GO
        INSERT INTO dbo.Keyed (Code) SELECT TOP ({RowCount}) CAST(ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS VARCHAR(20)) FROM sys.all_objects;
        GO
        CREATE FUNCTION dbo.BumpText(@x INT) RETURNS VARCHAR(20) WITH INLINE = OFF AS BEGIN RETURN CAST(@x + 1 AS VARCHAR(20)); END;
        GO
        CREATE FUNCTION dbo.BumpUnicode(@x INT) RETURNS NVARCHAR(20) WITH INLINE = OFF AS BEGIN RETURN CAST(@x + 1 AS NVARCHAR(20)); END;
        GO
        CREATE FUNCTION dbo.Pure(@x INT) RETURNS INT WITH SCHEMABINDING, INLINE = OFF AS BEGIN RETURN @x + 1; END;
        GO
        CREATE FUNCTION dbo.Reads(@x INT) RETURNS INT WITH SCHEMABINDING, INLINE = OFF AS BEGIN RETURN (SELECT MAX(V) FROM dbo.Source) + @x; END;
        GO
        CREATE FUNCTION dbo.Clocked(@x INT) RETURNS INT WITH SCHEMABINDING, INLINE = OFF AS BEGIN RETURN @x + DATEPART(ms, GETDATE()) * 0; END;
        GO
        CREATE TABLE dbo.Composite (A INT NOT NULL, B INT NOT NULL, V INT NOT NULL DEFAULT 0, PRIMARY KEY (A, B));
        GO
        INSERT INTO dbo.Composite (A, B) SELECT TOP ({RowCount}) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)), 0 FROM sys.all_objects;
        GO
        CREATE TABLE dbo.Covered (N INT NOT NULL, P INT NOT NULL DEFAULT 0);
        GO
        CREATE INDEX IX_Covered_N ON dbo.Covered (N);
        GO
        INSERT INTO dbo.Covered (N) SELECT TOP ({RowCount}) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) FROM sys.all_objects;
        GO
        """;

    private async Task<long> MaxActualRowsOfOperatorsCallingUdfAsync(string probe)
    {
        var plan = XDocument.Parse(await CaptureActualPlanAsync(probe));

        var counts = plan.Descendants()
            .Where(element => element.Name.LocalName == "RelOp")
            .Where(relOp => relOp.Descendants().Any(child => child.Name.LocalName == "UserDefinedFunction"))
            .SelectMany(relOp => relOp.Elements().Where(child => child.Name.LocalName == "RunTimeInformation"))
            .SelectMany(info => info.Descendants().Where(child => child.Name.LocalName == "RunTimeCountersPerThread"))
            .Select(counter => long.Parse((string)counter.Attribute("ActualRows")!, System.Globalization.CultureInfo.InvariantCulture))
            .ToList();

        Assert.NotEmpty(counts);
        return counts.Max();
    }

    [Theory]
    [InlineData("DECLARE @t TABLE (A INT); INSERT INTO @t (A) SELECT dbo.Bump(Id) FROM dbo.Source;")]
    [InlineData("SELECT dbo.Bump(Id) FROM dbo.Source;")]
    public async Task UdfInStatementWithRowSource_RunsOncePerRow(string statement)
    {
        Assert.Equal(RowCount, await MaxActualRowsOfOperatorsCallingUdfAsync(statement));
    }

    [Fact]
    [Trait("Rule", FindingRuleIds.ScalarUdfPredicateInvocationRuleId)]
    public async Task UdfInWhereWithRowSource_RunsOncePerRow()
    {
        Assert.Equal(RowCount, await MaxActualRowsOfOperatorsCallingUdfAsync("SELECT Id FROM dbo.Source WHERE dbo.Bump(Id) > 0;"));
    }

    [Fact]
    [Trait("Rule", FindingRuleIds.ScalarUdfPredicateInvocationRuleId)]
    public async Task UdfInWhereWithoutRowSource_RunsOnce()
    {
        Assert.Equal(1, await MaxActualRowsOfOperatorsCallingUdfAsync("SELECT 1 AS One WHERE dbo.Bump(1) = 2;"));
    }

    private async Task<long> UdfExecutionsAsync(string statement)
    {
        const string counter = "SELECT ISNULL(SUM(execution_count), 0) FROM sys.dm_exec_function_stats WHERE database_id = DB_ID() AND object_id IN (OBJECT_ID('dbo.Bump'), OBJECT_ID('dbo.BumpText'), OBJECT_ID('dbo.BumpUnicode'), OBJECT_ID('dbo.Pure'), OBJECT_ID('dbo.Reads'), OBJECT_ID('dbo.Clocked'));";
        var before = await ScalarAsync<long>(counter);
        await ExecuteAsync(statement);
        return await ScalarAsync<long>(counter) - before;
    }

    [Theory]
    [Trait("Rule", FindingRuleIds.ScalarUdfPredicateInvocationRuleId)]
    [InlineData("DECLARE @v INT = 1; SELECT COUNT(*) FROM dbo.Source WHERE dbo.Bump(@v) > 0 AND Id > 0;")]
    [InlineData("DECLARE @v INT = 1; SELECT COUNT(*) FROM dbo.Source WHERE @v < dbo.Bump(DATEPART(day, GETDATE()));")]
    [InlineData("DECLARE @v INT = 1; SELECT COUNT(*) FROM dbo.Source a JOIN dbo.Source b ON a.Id = b.Id AND dbo.Bump(@v) > 0;")]
    [InlineData("DECLARE @v INT = 1; SELECT COUNT(*) FROM dbo.Source WHERE CASE WHEN dbo.Bump(@v) > 0 THEN 1 ELSE 0 END = 1;")]
    public async Task UdfInPredicateConjunctWithoutAnyColumn_RunsOnce(string statement)
    {
        Assert.Equal(1, await UdfExecutionsAsync(statement));
    }

    [Theory]
    [Trait("Rule", FindingRuleIds.ScalarUdfPredicateInvocationRuleId)]
    [InlineData("DECLARE @v INT = 1; SELECT COUNT(*) FROM dbo.Source WHERE V < dbo.Bump(@v) + 1000;")]
    [InlineData("DECLARE @v INT = 1; SELECT COUNT(*) FROM dbo.Source WHERE V = 5 OR dbo.Bump(@v) > 100;")]
    [InlineData("DECLARE @v INT = 1; SELECT COUNT(*) FROM dbo.Source a JOIN dbo.Source b ON a.Id = b.Id AND a.V < dbo.Bump(@v) + 1000;")]
    public async Task UdfInPredicateConjunctWithAColumn_RunsOncePerRow(string statement)
    {
        Assert.True(await UdfExecutionsAsync(statement) >= RowCount);
    }

    [Theory]
    [Trait("Rule", FindingRuleIds.ScalarUdfPredicateInvocationRuleId)]
    [InlineData("DECLARE @v INT = 1; SELECT COUNT(*) FROM dbo.Source WHERE Id = dbo.Bump(@v);")]
    [InlineData("DECLARE @v INT = 1; SELECT SUM(V) FROM dbo.Source WHERE Id = dbo.Bump(@v);")]
    [InlineData("DECLARE @v INT = 1; SELECT SUM(V) FROM dbo.Source WHERE V >= 0 AND dbo.Bump(@v) = Id;")]
    [InlineData("DECLARE @v INT = 1; SELECT SUM(V) FROM dbo.Keyed WHERE Code = dbo.BumpText(@v);")]
    [InlineData("DECLARE @v INT = 1; UPDATE dbo.Source SET V = 0 WHERE Id = dbo.Bump(@v);")]
    public async Task UdfAsEqualityBoundOnSingleColumnUniqueKey_RunsOnce(string statement)
    {
        Assert.Equal(1, await UdfExecutionsAsync(statement));
    }

    [Theory]
    [Trait("Rule", FindingRuleIds.ScalarUdfPredicateInvocationRuleId)]
    [InlineData("DECLARE @v INT = 1; SELECT SUM(V) FROM dbo.Source WHERE Id = dbo.Bump(@v) OR V = 5;")]
    [InlineData("DECLARE @v INT = 1; SELECT SUM(V) FROM dbo.Source WHERE Id = dbo.Bump(V);")]
    [InlineData("DECLARE @v INT = 1; SELECT SUM(V) FROM dbo.Source WHERE V = dbo.Bump(@v);")]
    [InlineData("DECLARE @v INT = 1; SELECT SUM(V) FROM dbo.Source WITH (FORCESCAN) WHERE Id = dbo.Bump(@v);")]
    [InlineData("DECLARE @v INT = 1; SELECT SUM(V) FROM dbo.Keyed WHERE Code = dbo.BumpUnicode(@v);")]
    public async Task UdfInEqualityWithoutASingleColumnUniqueKeySeek_RunsOncePerRow(string statement)
    {
        Assert.True(await UdfExecutionsAsync(statement) >= RowCount);
    }

    [Fact]
    public async Task UdfInCrossApplyFunctionArgument_RunsOncePerOuterRow()
    {
        Assert.Equal(RowCount, await MaxActualRowsOfOperatorsCallingUdfAsync("SELECT w.V FROM dbo.Source s CROSS APPLY dbo.Wrap(dbo.Bump(s.Id)) w;"));
    }

    [Fact]
    public async Task UdfInUncorrelatedFunctionArgument_RunsOnce()
    {
        Assert.Equal(1, await MaxActualRowsOfOperatorsCallingUdfAsync("DECLARE @v INT = 1; SELECT w.V FROM dbo.Wrap(dbo.Bump(@v)) w;"));
    }

    [Theory]
    [InlineData("DECLARE @t TABLE (A INT); INSERT INTO @t (A) VALUES (dbo.Bump(1));")]
    [InlineData("DECLARE @t TABLE (A INT); INSERT INTO @t (A) SELECT dbo.Bump(1);")]
    public async Task UdfInStatementWithoutRowSource_RunsOnce(string statement)
    {
        Assert.Equal(1, await MaxActualRowsOfOperatorsCallingUdfAsync(statement));
    }

    private bool Reports(string statement)
    {
        var result = SqlScriptParser.ParseText("test.sql", Ddl + "\n" + statement);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        var catalog = CatalogBuilder.Build([result]);
        var (views, _) = ViewDefinitionExtractor.Extract([result], catalog.DefaultCollation, catalog.TypeAliases);
        return ScalarUdfScanner.Scan(result, catalog, ScalarUdfMap.Build(views, catalog))
            .Any(f => f.Kind is ScalarUdfFindingKind.PredicateInvocation or ScalarUdfFindingKind.ProjectionInvocation
                && f.Line > Ddl.AsSpan().Count('\n') + 1);
    }

    [Theory]
    [Trait("Rule", FindingRuleIds.ScalarUdfPredicateInvocationRuleId)]
    [InlineData("DECLARE @v INT = 1; SELECT COUNT(*) FROM dbo.Source WHERE V = dbo.Pure(@v);")]
    [InlineData("SELECT COUNT(*) FROM dbo.Source WHERE V + 0 > dbo.Pure(5);")]
    [InlineData("SELECT dbo.Pure(5) AS P FROM dbo.Source;")]
    [InlineData("UPDATE dbo.Source SET V = dbo.Pure(-1);")]
    [InlineData("DECLARE @v INT = 1; SELECT SUM(V) FROM dbo.Composite WHERE A = dbo.Bump(@v);")]
    [InlineData("DECLARE @v INT = 1; SELECT N FROM dbo.Covered WHERE N > dbo.Bump(@v);")]
    public async Task UdfTheEngineEvaluatesOncePerStatement_IsNotReported(string statement)
    {
        Assert.Equal(1, await UdfExecutionsAsync(statement));
        Assert.False(Reports(statement));
    }

    [Theory]
    [Trait("Rule", FindingRuleIds.ScalarUdfPredicateInvocationRuleId)]
    [InlineData("SELECT COUNT(*) FROM dbo.Source WHERE V = dbo.Reads(5);")]
    [InlineData("SELECT COUNT(*) FROM dbo.Source WHERE V = dbo.Clocked(5);")]
    [InlineData("SELECT CASE WHEN V >= 0 THEN dbo.Pure(5) ELSE 0 END AS P FROM dbo.Source;")]
    [InlineData("SELECT IIF(V >= 0, dbo.Pure(5), 0) AS P FROM dbo.Source;")]
    [InlineData("SELECT dbo.Pure(V) AS P FROM dbo.Source;")]
    [InlineData("DECLARE @v INT = 0; SELECT @v = dbo.Pure(@v) FROM dbo.Source;")]
    public async Task UdfTheEngineEvaluatesForEveryRow_IsReported(string statement)
    {
        Assert.True(await UdfExecutionsAsync(statement) >= RowCount);
        Assert.True(Reports(statement));
    }
}
