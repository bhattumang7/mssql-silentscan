using SilentScan.Core.Catalog;
using SilentScan.Core.Diagnostics;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;
using SilentScan.Core.Predicates.DynamicSqlValue;
using SilentScan.Core.Reporting.RuleHarness;

namespace SilentScan.Tests.Predicates;

public sealed class DynamicSqlRuleParityTests
{
    private static DynamicSqlPipelineResult Run(string sql, bool withCrossBoundaryTempTableSeeding = false)
    {
        var parseResult = SqlScriptParser.ParseText("test.sql", sql);
        Assert.False(parseResult.HasErrors, string.Join("; ", parseResult.Errors.Select(e => e.Message)));

        var catalog = CatalogBuilder.Build([parseResult]);
        var lineage = LineageResolver.Resolve(catalog, [parseResult]);
        var procCallGraph = ProcCallGraphBuilder.Build([parseResult], catalog, new SkipLedger());

        var extraction = DynamicSqlScannerV2.Scan(parseResult, callGraph: procCallGraph);
        Assert.NotEmpty(extraction.AnalyzableScripts);

        var ruleContext = new RuleContext(
            catalog, lineage, new SkipLedger(), procCallGraph,
            new Dictionary<string, TvfFenceOrigin>(), new Dictionary<string, ScalarUdfOrigin>(),
            new Dictionary<string, ViewExpansionOrigin>(), [], new Dictionary<string, SelectStarViewCandidate>(),
            new Dictionary<string, IReadOnlyList<string>>());

        var outerTempTableDeclarationsByScope = withCrossBoundaryTempTableSeeding
            ? UnindexedTempTableUsageScanner.CollectDeclarationsByScope([parseResult], catalog)
            : null;

        return DynamicSqlPipeline.Analyze(
            extraction.AnalyzableScripts, catalog, lineage,
            new Dictionary<string, TvfFenceOrigin>(), new Dictionary<string, ScalarUdfOrigin>(),
            null, new DynamicSqlPipeline.DynamicSqlHarnessOptions(ruleContext, outerTempTableDeclarationsByScope));
    }

    [Fact]
    public void WaitForInsideExecString_FiresAtOuterCallSite()
    {
        const string sql = """
            CREATE PROCEDURE dbo.RunDelayed
            AS
            BEGIN
                EXEC('WAITFOR DELAY ''00:00:05'';');
            END
            """;

        var result = Run(sql);

        var findings = result.HarnessFindings!["WaitForScanner"];
        var finding = Assert.Single(findings);
        var waitFor = Assert.IsType<WaitForFinding>(finding);
        Assert.NotNull(waitFor.DynamicSqlCallSite);
        Assert.Equal("test.sql", waitFor.SourcePath);
        Assert.Equal(4, waitFor.Line);
    }

    [Fact]
    public void CartesianJoinInsideLiteralExecString_FiresAtOuterCallSite()
    {
        const string sql = """
            CREATE PROCEDURE dbo.RunCartesian
            AS
            BEGIN
                EXEC('SELECT * FROM dbo.A, dbo.B;');
            END
            """;

        var result = Run(sql);

        var findings = result.HarnessFindings!["CartesianJoinScanner"];
        var finding = Assert.Single(findings);
        var cartesian = Assert.IsType<CartesianJoinFinding>(finding);
        Assert.Equal(CartesianJoinKind.CommaJoin, cartesian.Kind);
        Assert.NotNull(cartesian.DynamicSqlCallSite);
    }

    [Fact]
    public void NoWaitForInsideExecString_NeverFires()
    {
        const string sql = """
            CREATE PROCEDURE dbo.RunNow
            AS
            BEGIN
                EXEC('SELECT 1;');
            END
            """;

        var result = Run(sql);

        Assert.True(result.HarnessFindings is null || !result.HarnessFindings.ContainsKey("WaitForScanner"));
    }

    [Fact]
    public void OuterTempTableDeclaredThenJoinedInsideSpExecuteSql_NoIndex_FiresAtOuterCallSite()
    {
        const string sql = """
            CREATE PROCEDURE dbo.RunJoin
            AS
            BEGIN
                SELECT Id, Code INTO #t FROM dbo.Source WHERE Flag = 1;
                EXEC sp_executesql N'SELECT s.* FROM dbo.Source2 s INNER JOIN #t t ON s.Code = t.Code;';
            END
            """;

        var result = Run(sql, withCrossBoundaryTempTableSeeding: true);

        var findings = result.HarnessFindings!["UnindexedTempTableUsageScanner"];
        var finding = Assert.Single(findings);
        var tempTable = Assert.IsType<UnindexedTempTableUsageFinding>(finding);
        Assert.Equal(UnindexedTempTableUsageKind.JoinOperand, tempTable.Kind);
        Assert.Equal(4, tempTable.DeclarationLine);
        Assert.Equal(5, tempTable.UsageLine);
        Assert.NotNull(tempTable.DynamicSqlCallSite);
    }

    [Fact]
    public void OuterTempTableDeclaredWithIndexThenJoinedInsideSpExecuteSql_NeverFires()
    {
        const string sql = """
            CREATE PROCEDURE dbo.RunJoin
            AS
            BEGIN
                SELECT Id, Code INTO #t FROM dbo.Source WHERE Flag = 1;
                CREATE INDEX IX_t ON #t (Code);
                EXEC sp_executesql N'SELECT s.* FROM dbo.Source2 s INNER JOIN #t t ON s.Code = t.Code;';
            END
            """;

        var result = Run(sql, withCrossBoundaryTempTableSeeding: true);

        Assert.True(result.HarnessFindings is null || !result.HarnessFindings.ContainsKey("UnindexedTempTableUsageScanner"));
    }

    [Fact]
    public void OuterTempTableDeclaredThenJoinedInsideSpExecuteSql_SeedingDisabled_NeverFires()
    {
        const string sql = """
            CREATE PROCEDURE dbo.RunJoin
            AS
            BEGIN
                SELECT Id, Code INTO #t FROM dbo.Source WHERE Flag = 1;
                EXEC sp_executesql N'SELECT s.* FROM dbo.Source2 s INNER JOIN #t t ON s.Code = t.Code;';
            END
            """;

        var result = Run(sql, withCrossBoundaryTempTableSeeding: false);

        Assert.True(result.HarnessFindings is null || !result.HarnessFindings.ContainsKey("UnindexedTempTableUsageScanner"));
    }
}
