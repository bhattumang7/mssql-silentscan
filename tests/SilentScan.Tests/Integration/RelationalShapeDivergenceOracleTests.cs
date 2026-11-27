using SilentScan.Core.Rules;
using System.Xml.Linq;
using SilentScan.Core.Catalog;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class RelationalShapeDivergenceOracleTests : EngineDivergenceOracleTestBase
{
    protected override string DatabaseNameSeed => nameof(RelationalShapeDivergenceOracleTests);

    private async Task RunBatchesAsync(string script)
    {
        foreach (var batch in script.Split("\nGO", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            await ExecuteAsync(batch);
        }
    }

    [Theory]
    [InlineData("CUBE(a)", 1)]
    [InlineData("ROLLUP(a)", 1)]
    [InlineData("GROUPING SETS ((a), ())", 1)]
    [InlineData("a", 0)]
    [Trait("Rule", FindingRuleIds.NotNullPredicateContradictionRuleId)]
    public async Task SuperAggregateGrouping_MakesANotNullColumnNullInTheOuterQuery(string grouping, int rows)
    {
        const string ddl = "CREATE TABLE dbo.H (a INT NOT NULL, b INT NOT NULL);";
        var probe = $"SELECT COUNT(*) FROM (SELECT a, SUM(b) AS s FROM dbo.H GROUP BY {grouping}) d WHERE d.a IS NULL;";
        await RunBatchesAsync($"{ddl}\nGO\nINSERT dbo.H VALUES (1, 1), (2, 2);");

        Assert.Equal(rows, await ScalarAsync<int>(probe));

        var (result, catalog) = Parse($"{ddl}\nGO\n{probe}");
        Assert.Equal(rows == 0, CheckConstraintPredicateContradictionScanner.Scan(result, catalog).Count > 0);
    }

    [Fact]
    [Trait("Rule", FindingRuleIds.CartesianJoinCommaJoinRuleId)]
    public async Task GraphMatch_ConnectsCommaListedTables_SoNoProductIsReported()
    {
        const string ddl = """
            CREATE TABLE dbo.Person (Id INT NOT NULL) AS NODE;
            GO
            CREATE TABLE dbo.City (Id INT NOT NULL) AS NODE;
            GO
            CREATE TABLE dbo.Lives AS EDGE;
            """;
        const string matched = "SELECT COUNT(*) FROM dbo.Person p, dbo.Lives l, dbo.City c WHERE MATCH(p-(l)->c)";
        const string unmatched = "SELECT COUNT(*) FROM dbo.Person p, dbo.Lives l, dbo.City c";
        await RunBatchesAsync(ddl);
        await RunBatchesAsync("""
            INSERT dbo.Person (Id) VALUES (1), (2), (3);
            INSERT dbo.City (Id) VALUES (1), (2), (3);
            INSERT dbo.Lives ($from_id, $to_id)
            SELECT p.$node_id, c.$node_id FROM dbo.Person p JOIN dbo.City c ON c.Id = p.Id;
            """);

        Assert.Equal(3, await ScalarAsync<int>(matched + ";"));
        Assert.Equal(27, await ScalarAsync<int>(unmatched + ";"));

        bool Reports(string query)
        {
            var (result, catalog) = Parse($"{ddl}\nGO\n{query};");
            return CartesianJoinScanner.Scan(result, catalog).Count > 0;
        }

        Assert.False(Reports(matched));
        Assert.True(Reports(unmatched));
    }

    private const string TvfDdl = """
        CREATE TABLE dbo.Outer50 (i INT NOT NULL);
        GO
        CREATE FUNCTION dbo.Five(@x INT) RETURNS @r TABLE (v INT) AS BEGIN INSERT @r VALUES (1), (2), (3), (4), (5); RETURN; END
        GO
        CREATE VIEW dbo.OverFive AS SELECT v FROM dbo.Five(1);
        GO
        CREATE FUNCTION dbo.Pass(@a INT, @b INT) RETURNS TABLE AS RETURN SELECT v, @a AS a FROM dbo.Five(@b);
        """;

    private async Task<List<XElement>> TvfOperatorsAsync(string probe)
    {
        await RunBatchesAsync(TvfDdl);
        await ExecuteAsync("INSERT dbo.Outer50 SELECT TOP (50) ROW_NUMBER() OVER (ORDER BY (SELECT 1)) FROM sys.all_objects;");
        var operators = await ActualRelOpsAsync(probe, "dbo.");
        return [.. operators.Where(o => (string?)o.Attribute("PhysicalOp") == "Table-valued function")];
    }

    private static (bool Correlated, bool Nested) TvfFindings(string probe)
    {
        var result = SqlScriptParser.ParseText("test.sql", $"{TvfDdl}\nGO\n{probe}");
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        var catalog = CatalogBuilder.Build([result]);
        catalog.CompatibilityLevel = 170;
        var (views, _) = ViewDefinitionExtractor.Extract([result], catalog.DefaultCollation, catalog.TypeAliases);
        var findings = TvfFenceScanner.Scan(result, catalog, TvfFenceMap.Build(views, catalog))
            .Where(f => f.Line > TvfDdl.AsSpan().Count('\n') + 1)
            .ToList();
        return (
            findings.Any(f => f.Kind == TvfFenceFindingKind.CorrelatedApply),
            findings.Any(f => f.Kind == TvfFenceFindingKind.NestedUnderViewOrTvf));
    }

    [Theory]
    [InlineData("(SELECT MAX(i) FROM dbo.Outer50)", false)]
    [InlineData("s.i", true)]
    [InlineData("(SELECT MAX(o.i) FROM dbo.Outer50 o WHERE o.i <= s.i)", true)]
    [Trait("Rule", FindingRuleIds.TvfFenceCorrelatedApplyRuleId)]
    public async Task ApplyReExecutesTheFunction_OnlyWhenAnArgumentReadsTheOuterRow(string argument, bool perRow)
    {
        var probe = $"SELECT f.v FROM dbo.Outer50 s CROSS APPLY dbo.Five({argument}) f;";

        var operators = await TvfOperatorsAsync(probe);

        var executions = operators.Max(o => o.Descendants()
            .Where(e => e.Name.LocalName == "RunTimeCountersPerThread")
            .Sum(e => (int)e.Attribute("ActualExecutions")!));
        Assert.Equal(perRow, executions > 1);
        Assert.Equal(perRow, TvfFindings(probe).Correlated);
    }

    [Theory]
    [InlineData("SELECT v FROM dbo.OverFive;", false)]
    [InlineData("IF EXISTS (SELECT 1 FROM dbo.OverFive WHERE v = -1) PRINT 1;", true)]
    [InlineData("WHILE (SELECT COUNT(*) FROM dbo.OverFive) < 0 PRINT 1;", true)]
    [InlineData("SELECT p.v FROM dbo.Outer50 s CROSS APPLY dbo.Pass(7, s.i) p;", true)]
    [InlineData("SELECT p.v FROM dbo.Outer50 s CROSS APPLY dbo.Pass(s.i, 7) p;", false)]
    [InlineData("SELECT p.v FROM dbo.Pass(1, (SELECT MAX(i) FROM dbo.Outer50)) p;", true)]
    [InlineData("SELECT p.v FROM dbo.Pass((SELECT MAX(i) FROM dbo.Outer50), 1) p;", false)]
    [Trait("Rule", FindingRuleIds.TvfFenceNestedUnderViewOrTvfRuleId)]
    public async Task NestedMultiStatementFunction_KeepsTheFixedEstimate_ExactlyWhereReported(string probe, bool fixedEstimate)
    {
        var operators = await TvfOperatorsAsync(probe);

        Assert.NotEmpty(operators);
        Assert.Equal(
            fixedEstimate,
            operators.Any(o => double.Parse((string)o.Attribute("EstimateRows")!, System.Globalization.CultureInfo.InvariantCulture) > 99));
        Assert.Equal(fixedEstimate, TvfFindings(probe).Nested);
    }

    [Theory]
    [InlineData("dbo.OverCatalog", true)]
    [InlineData("dbo.OverOverCatalog", true)]
    [InlineData("dbo.OverUserTable", false)]
    [Trait("Rule", FindingRuleIds.ForcedSerialNonParallelizableIntrinsicRuleId)]
    public async Task QueryOverAViewOfACatalogView_IsAlreadySerial_SoTheIntrinsicIsNotReported(string view, bool alreadySerial)
    {
        const string ddl = """
            CREATE TABLE dbo.Plain (object_id INT NOT NULL);
            GO
            CREATE VIEW dbo.OverCatalog AS SELECT object_id FROM sys.objects;
            GO
            CREATE VIEW dbo.OverOverCatalog AS SELECT object_id FROM dbo.OverCatalog;
            GO
            CREATE VIEW dbo.OverUserTable AS SELECT object_id FROM dbo.Plain;
            """;
        await RunBatchesAsync(ddl);

        var plan = await CaptureActualPlanAsync(
            $"SELECT object_id, COUNT(*) FROM {view} GROUP BY object_id OPTION (USE HINT('ENABLE_PARALLEL_PLAN_PREFERENCE'));");
        Assert.Equal(alreadySerial, plan.Contains("NonParallelPlanReason=\"NonParallelizableIntrinsicFunction\"", StringComparison.Ordinal));

        var result = SqlScriptParser.ParseText("test.sql", $"{ddl}\nGO\nSELECT object_id FROM {view} WHERE OBJECT_ID('dbo.Plain') IS NOT NULL;");
        var catalog = CatalogBuilder.Build([result]);
        var (views, _) = ViewDefinitionExtractor.Extract([result], catalog.DefaultCollation, catalog.TypeAliases);
        Assert.Equal(
            !alreadySerial,
            ForcedSerialScanner.Scan(result, views).Any(f => f.Kind == ForcedSerialFindingKind.NonParallelizableIntrinsic));
    }
}
