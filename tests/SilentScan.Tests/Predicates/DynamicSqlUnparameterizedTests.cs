using SilentScan.Core.Catalog;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;
using SilentScan.Core.Predicates.DynamicSqlValue;

namespace SilentScan.Tests.Predicates;

public sealed class DynamicSqlUnparameterizedTests
{
    private const string SchemaSql = "CREATE TABLE dbo.T (Code VARCHAR(20) NOT NULL, Name VARCHAR(50) NOT NULL);";

    private static (DatabaseCatalog Catalog, LineageCatalog Lineage) BuildCatalog()
    {
        var schema = SqlScriptParser.ParseText("schema.sql", SchemaSql);
        Assert.False(schema.HasErrors, string.Join("; ", schema.Errors.Select(e => e.Message)));

        var catalog = CatalogBuilder.Build([schema]);
        var lineage = LineageResolver.Resolve(catalog, [schema]);
        return (catalog, lineage);
    }

    private static DynamicSqlPipelineResult Analyze(string appSql)
    {
        var (catalog, lineage) = BuildCatalog();
        var parseResult = SqlScriptParser.ParseText("app.sql", appSql);
        Assert.False(parseResult.HasErrors, string.Join("; ", parseResult.Errors.Select(e => e.Message)));

        var extraction = DynamicSqlScannerV2.Scan(parseResult);
        var script = Assert.Single(extraction.AnalyzableScripts);
        return DynamicSqlPipeline.Analyze([script], catalog, lineage);
    }

    [Fact]
    public void ExecStringConcatenatesLiteralValue_FiresBothKinds()
    {
        var appSql =
            "CREATE PROCEDURE dbo.usp_Find AS\n" +
            "BEGIN\n" +
            "    DECLARE @Code VARCHAR(20) = 'ABC'\n" +
            "    EXEC('SELECT * FROM dbo.T a JOIN dbo.T b ON a.Name = b.Name WHERE a.Code = ''' + @Code + '''')\n" +
            "END\n";

        var result = Analyze(appSql);

        Assert.Equal(2, result.UnparameterizedFindings.Count);
        Assert.Contains(result.UnparameterizedFindings, f => f.Kind == UnparameterizedDynamicSqlFindingKind.ConcatenatedValueInConstantSql);
        Assert.Contains(result.UnparameterizedFindings, f => f.Kind == UnparameterizedDynamicSqlFindingKind.ExecStringConcatenatesParameterizableValue);
        Assert.All(result.UnparameterizedFindings, f =>
        {
            Assert.Equal("app.sql", f.SourcePath);
            Assert.Equal(4, f.Line);
        });
    }

    [Fact]
    public void SpExecuteSqlConcatenatesLiteralValueIntoTextInsteadOfUsingParams_FiresGeneralKindOnly()
    {

        var appSql =
            "CREATE PROCEDURE dbo.usp_Find AS\n" +
            "BEGIN\n" +
            "    DECLARE @Code VARCHAR(20) = 'ABC'\n" +
            "    DECLARE @sql NVARCHAR(MAX) = N'SELECT * FROM dbo.T a JOIN dbo.T b ON a.Name = b.Name WHERE a.Code = ''' + @Code + ''''\n" +
            "    EXEC sp_executesql @sql\n" +
            "END\n";

        var result = Analyze(appSql);

        var finding = Assert.Single(result.UnparameterizedFindings);
        Assert.Equal(UnparameterizedDynamicSqlFindingKind.ConcatenatedValueInConstantSql, finding.Kind);
    }

    [Theory]
    [InlineData("SELECT * FROM dbo.T WHERE Code = ''' + @Code + '''")]
    [InlineData("DELETE dbo.T WHERE Code = ''' + @Code + '''")]
    [InlineData("INSERT dbo.T (Code, Name) VALUES (''' + @Code + ''', ''n'')")]
    public void ConcatenatedValueInAStatementTheEngineCanAutoParameterize_NeverFires(string dynamicSql)
    {
        var appSql =
            "CREATE PROCEDURE dbo.usp_Find AS\n" +
            "BEGIN\n" +
            "    DECLARE @Code VARCHAR(20) = 'ABC'\n" +
            $"    EXEC('{dynamicSql}')\n" +
            "END\n";

        Assert.Empty(Analyze(appSql).UnparameterizedFindings);
    }

    [Theory]
    [InlineData("SELECT * FROM dbo.T WHERE Code = ''' + @Code + ''' OPTION (MAXDOP 1)")]
    [InlineData("SELECT Name, COUNT(*) FROM dbo.T WHERE Code = ''' + @Code + ''' GROUP BY Name")]
    [InlineData("SELECT TOP (5) * FROM dbo.T WHERE Code = ''' + @Code + '''")]
    [InlineData("SELECT DISTINCT Name FROM dbo.T WHERE Code = ''' + @Code + '''")]
    [InlineData("SELECT * FROM dbo.T WHERE Name IN (SELECT Name FROM dbo.T WHERE Code = ''' + @Code + ''')")]
    public void ConcatenatedValueInAStatementTheEngineCannotAutoParameterize_Fires(string dynamicSql)
    {
        var appSql =
            "CREATE PROCEDURE dbo.usp_Find AS\n" +
            "BEGIN\n" +
            "    DECLARE @Code VARCHAR(20) = 'ABC'\n" +
            $"    EXEC('{dynamicSql}')\n" +
            "END\n";

        Assert.Contains(Analyze(appSql).UnparameterizedFindings, f => f.Kind == UnparameterizedDynamicSqlFindingKind.ConcatenatedValueInConstantSql);
    }

    [Fact]
    public void ExecOfWholeSingleLiteral_NoConcatenation_NeverFires()
    {
        var appSql = "EXEC('SELECT * FROM dbo.T WHERE Code = ''ABC''');";

        var result = Analyze(appSql);

        Assert.Empty(result.UnparameterizedFindings);
    }

    [Fact]
    public void ExecStringConcatenatesIdentifierNotValue_NeverFires()
    {

        var appSql =
            "CREATE PROCEDURE dbo.usp_Find AS\n" +
            "BEGIN\n" +
            "    DECLARE @TableName VARCHAR(20) = 'T'\n" +
            "    EXEC('SELECT * FROM dbo.' + @TableName)\n" +
            "END\n";

        var result = Analyze(appSql);

        Assert.Empty(result.UnparameterizedFindings);
    }

    [Theory]
    [InlineData("SELECT TOP (1) Name FROM dbo.T WHERE Code = ''' + @Code + '''", true)]
    [InlineData("SELECT Name FROM dbo.T WHERE Code = ''' + @Code + ''' FOR XML PATH", false)]
    [InlineData("SELECT Name FROM dbo.T WHERE Code = ''' + @Code + ''' FOR JSON PATH", false)]
    [InlineData("SELECT TOP (1) Name FROM dbo.T WHERE Code = ''' + @Code + ''' OPTION (RECOMPILE)", false)]
    public void OnlyShapesTheEngineLeavesUnparameterizedAreReported(string inner, bool fires)
    {
        var appSql =
            "CREATE PROCEDURE dbo.usp_Find AS\n"
            + "BEGIN\n"
            + "    DECLARE @Code VARCHAR(20) = 'ABC'\n"
            + $"    EXEC('{inner}')\n"
            + "END\n";

        Assert.Equal(fires, Analyze(appSql).UnparameterizedFindings.Count > 0);
    }
}
