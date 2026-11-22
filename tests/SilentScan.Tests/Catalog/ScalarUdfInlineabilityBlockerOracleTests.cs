using SilentScan.Core.Catalog;
using SilentScan.Core.Parsing;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Catalog;

[Trait("Category", "Oracle")]
public sealed class ScalarUdfInlineabilityBlockerOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(ScalarUdfInlineabilityBlockerOracleTests);

    protected override string Ddl => """
        CREATE TABLE dbo.Anchor (Id INT NOT NULL);
        GO
        CREATE FUNCTION dbo.fn_Base (@x INT) RETURNS INT AS BEGIN RETURN @x; END
        GO
        """;

    [Theory]
    [InlineData("while", "DECLARE @i INT = 0; WHILE @i < 3 SET @i += 1; RETURN @i;")]
    [InlineData("cursor", "DECLARE @v INT = 0; DECLARE c CURSOR LOCAL FOR SELECT Id FROM dbo.Anchor; OPEN c; FETCH NEXT FROM c INTO @v; CLOSE c; DEALLOCATE c; RETURN @v;")]
    [InlineData("tablevar", "DECLARE @t TABLE (a INT); INSERT @t VALUES (1); RETURN (SELECT COUNT(*) FROM @t);")]
    [InlineData("subquery", "RETURN (SELECT COUNT(*) FROM dbo.Anchor WHERE Id = @x);")]
    [InlineData("assignselect", "DECLARE @v INT; SELECT @v = MAX(Id) FROM dbo.Anchor; RETURN @v;")]
    [InlineData("assignselect_order", "DECLARE @v INT = 0; SELECT @v = @v + Id FROM dbo.Anchor ORDER BY Id; RETURN @v;")]
    [InlineData("multireturn", "IF @x > 0 RETURN 1; RETURN 0;")]
    [InlineData("goto", "DECLARE @v INT = 0; GOTO done; SET @v = 1; done: RETURN @v;")]
    [InlineData("topselect", "RETURN (SELECT TOP (1) Id FROM dbo.Anchor ORDER BY Id);")]
    [InlineData("window", "RETURN (SELECT TOP (1) ROW_NUMBER() OVER (ORDER BY Id) FROM dbo.Anchor);")]
    [InlineData("forxml", "DECLARE @s VARCHAR(100) = (SELECT CAST(Id AS VARCHAR(10)) + ',' FROM dbo.Anchor FOR XML PATH('')); RETURN LEN(@s);")]
    [InlineData("stringagg", "RETURN (SELECT LEN(STRING_AGG(CAST(Id AS VARCHAR(10)), ',')) FROM dbo.Anchor);")]
    [InlineData("udfcall", "RETURN dbo.fn_Base(@x) + 1;")]
    [InlineData("xmlvalue", "DECLARE @d XML = '<a>1</a>'; RETURN @d.value('(/a)[1]', 'int');")]
    [InlineData("xmlexist", "DECLARE @d XML = '<a>1</a>'; RETURN @d.exist('/a');")]
    [InlineData("getdate", "RETURN DATEPART(YEAR, GETDATE());")]
    [InlineData("case", "RETURN CASE WHEN @x > 0 THEN 1 ELSE 0 END;")]
    [InlineData("coalesce", "RETURN COALESCE(@x, 0);")]
    [InlineData("exists", "RETURN CASE WHEN EXISTS (SELECT 1 FROM dbo.Anchor) THEN 1 ELSE 0 END;")]
    [InlineData("setonly", "DECLARE @v INT; SET @v = @x * 2; RETURN @v;")]
    [InlineData("ifelse", "DECLARE @v INT; IF @x > 0 SET @v = 1; ELSE SET @v = 2; RETURN @v;")]
    [InlineData("nested_if_ret", "IF @x > 0 BEGIN IF @x > 5 RETURN 2; RETURN 1; END RETURN 0;")]
    [InlineData("convertdate", "RETURN DATEPART(YEAR, CONVERT(DATETIME, '2020-01-01'));")]
    [InlineData("random", "RETURN ABS(CHECKSUM(@x));")]
    [InlineData("openjson", "RETURN (SELECT COUNT(*) FROM OPENJSON('[1,2]'));")]
    [InlineData("userinline", "DECLARE @v INT = (SELECT MAX(Id) FROM dbo.Anchor); RETURN @v;")]
    [InlineData("scopeid", "RETURN CAST(SCOPE_IDENTITY() AS INT);")]
    [InlineData("rowcount", "RETURN @@ROWCOUNT;")]
    [InlineData("pivot", "RETURN (SELECT COUNT(*) FROM (SELECT Id, 1 AS k FROM dbo.Anchor) s PIVOT (COUNT(k) FOR Id IN ([1],[2])) p);")]
    [InlineData("groupby", "RETURN (SELECT TOP (1) COUNT(*) FROM dbo.Anchor GROUP BY Id);")]
    [InlineData("union", "RETURN (SELECT COUNT(*) FROM (SELECT Id FROM dbo.Anchor UNION SELECT Id FROM dbo.Anchor) u);")]
    public async Task WhenTheScannerPredictsABlocker_TheEngineReportsTheFunctionAsNotInlineable(string name, string body)
    {
        var ddl = $"CREATE FUNCTION dbo.fn_Probe (@x INT) RETURNS INT AS BEGIN {body} END";
        await ExecuteAsync(ddl);
        var engineInlineable = await ScalarAsync<int>("SELECT CAST(is_inlineable AS INT) FROM sys.sql_modules WHERE object_id = OBJECT_ID('dbo.fn_Probe');");

        var parsed = SqlScriptParser.ParseText("test.sql", Ddl + Environment.NewLine + ddl);
        Assert.False(parsed.HasErrors, string.Join("; ", parsed.Errors.Select(e => e.Message)));
        var catalog = CatalogBuilder.Build([parsed]);
        var predictedBlocker = catalog.TryGetScalarUdfInfo("dbo.fn_Probe", out var info) ? info!.InlineabilityBlocker : null;

        Assert.True(
            predictedBlocker is null || engineInlineable == 0,
            $"{name}: scanner predicts blocker '{predictedBlocker}' but engine is_inlineable = {engineInlineable}");
    }
}
