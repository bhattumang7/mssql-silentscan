using SilentScan.Core.Rules;
using System.Globalization;
using System.Xml.Linq;
using SilentScan.Core.Catalog;
using SilentScan.Core.Lineage;
using SilentScan.Core.Predicates;
using SilentScan.Core.TypeInference;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class StatementShapeDivergenceOracleTests : EngineDivergenceOracleTestBase
{
    protected override string DatabaseNameSeed => nameof(StatementShapeDivergenceOracleTests);

    [Theory]
    [InlineData("1E2")]
    [InlineData("100.0E0")]
    [InlineData("0100")]
    [Trait("Rule", FindingRuleIds.BareTopNoOrderByRuleId)]
    [Trait("Rule", FindingRuleIds.ViewOrderingTopPercentOrderByNeverLimitsRuleId)]
    public async Task EverySpellingOfOneHundredPercent_ReturnsAllRows_AndIsTreatedAsNonLimiting(string spelling)
    {
        Assert.Equal(3, await ScalarAsync<int>($"SELECT COUNT(*) FROM (SELECT TOP ({spelling}) PERCENT v FROM (VALUES (1),(2),(3)) t(v)) d;"));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM (SELECT TOP (1E1) PERCENT v FROM (VALUES (1),(2),(3)) t(v)) d;"));

        var (bare, _) = Parse($"SELECT TOP ({spelling}) PERCENT v FROM dbo.T;");
        Assert.Empty(BareTopNoOrderByScanner.Scan(bare));
        var (limiting, _) = Parse("SELECT TOP (1E1) PERCENT v FROM dbo.T;");
        Assert.Single(BareTopNoOrderByScanner.Scan(limiting));

        var (view, _) = Parse($"CREATE VIEW dbo.v1 AS SELECT TOP ({spelling}) PERCENT v FROM dbo.T ORDER BY v;");
        Assert.Equal(
            ViewOrderingFindingKind.TopPercentOrderByNeverLimits,
            Assert.Single(ViewOrderingScanner.Scan(view)).Kind);
    }

    [Fact]
    [Trait("Rule", FindingRuleIds.IndexDesignSubsumedIndexRuleId)]
    public async Task UniquePrefixIndex_EnforcesWhatTheWiderUniqueIndexDoesNot_AndIsNotReportedAsSubsumed()
    {
        await ExecuteAsync("""
            CREATE TABLE dbo.U (a INT NOT NULL, b INT NOT NULL);
            CREATE UNIQUE INDEX UX_a ON dbo.U(a);
            CREATE UNIQUE INDEX UX_ab ON dbo.U(a, b);
            INSERT dbo.U VALUES (1, 1);
            """);
        Assert.Equal(2601, await SqlErrorNumberAsync("INSERT dbo.U VALUES (1, 2);"));
        await ExecuteAsync("DROP INDEX UX_a ON dbo.U; INSERT dbo.U VALUES (1, 3);");
        Assert.Equal(2, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.U WHERE a = 1;"));

        var (_, unique) = Parse("CREATE TABLE dbo.U (a INT NOT NULL, b INT NOT NULL); CREATE UNIQUE INDEX UX_a ON dbo.U(a); CREATE UNIQUE INDEX UX_ab ON dbo.U(a, b);");
        Assert.DoesNotContain(IndexDesignScanner.Scan(unique), f => f.Kind == IndexDesignFindingKind.SubsumedIndex);
        var (_, plain) = Parse("CREATE TABLE dbo.U (a INT NOT NULL, b INT NOT NULL); CREATE INDEX IX_a ON dbo.U(a); CREATE INDEX IX_ab ON dbo.U(a, b);");
        Assert.Contains(IndexDesignScanner.Scan(plain), f => f.Kind == IndexDesignFindingKind.SubsumedIndex);
    }

    [Fact]
    [Trait("Rule", FindingRuleIds.QueryAntiPatternTableVariableStaleEstimateInLoopRuleId)]
    public async Task LoopReadWithOptionRecompile_EstimatesTheCurrentRowCount_AndIsNotReported()
    {
        const string loop = """
            DECLARE @t TABLE (a INT); DECLARE @i INT = 0; DECLARE @c INT;
            WHILE @i < 40
            BEGIN
                INSERT @t VALUES (@i); SET @i += 1;
                SELECT @c = COUNT(*) FROM @t AS hinted OPTION (RECOMPILE);
                SELECT @c = COUNT(*) FROM @t AS plain WHERE a >= 0;
            END;
            """;
        var relOps = await ActualRelOpsAsync(loop, "COUNT(*) FROM @t");
        double MaxEstimate(string alias) => relOps
            .Where(op => op.Attribute("PhysicalOp")!.Value == "Table Scan"
                && op.Descendants().Any(e => e.Name.LocalName == "Object" && (string?)e.Attribute("Alias") == $"[{alias}]"))
            .Max(op => double.Parse(op.Attribute("EstimateRows")!.Value, CultureInfo.InvariantCulture));
        Assert.True(MaxEstimate("hinted") >= 39, $"hinted estimate was {MaxEstimate("hinted")}");
        Assert.True(MaxEstimate("plain") <= 2, $"plain estimate was {MaxEstimate("plain")}");

        var (result, catalog) = Parse(loop);
        catalog.CompatibilityLevel = 160;
        var finding = Assert.Single(
            QueryAntiPatternScanner.Scan(result, catalog),
            f => f.Kind == QueryAntiPatternFindingKind.TableVariableStaleEstimateInLoop);
        Assert.Equal(loop.Split('\n').ToList().FindIndex(l => l.Contains("AS plain", StringComparison.Ordinal)) + 1, finding.Line);
    }

    [Theory]
    [InlineData(160, "QUERY_OPTIMIZER_COMPATIBILITY_LEVEL_140", true)]
    [InlineData(160, null, false)]
    [InlineData(140, "QUERY_OPTIMIZER_COMPATIBILITY_LEVEL_150", false)]
    [InlineData(140, null, true)]
    [Trait("Rule", FindingRuleIds.QueryAntiPatternTableVariableLowCompatEstimateRuleId)]
    public async Task StatementCompatibilityHint_ReplacesTheDatabaseLevelForTableVariableEstimates(int databaseLevel, string? hint, bool fixedOneRow)
    {
        await ExecuteAsync($"ALTER DATABASE CURRENT SET COMPATIBILITY_LEVEL = {databaseLevel};");
        var option = hint is null ? string.Empty : $" OPTION (USE HINT('{hint}'))";
        var read = $"SELECT COUNT(*) FROM @t a JOIN @t b ON a.id = b.id{option};";
        var batch = $"""
            DECLARE @t TABLE (id INT PRIMARY KEY);
            INSERT @t SELECT TOP (3000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) FROM sys.all_columns x CROSS JOIN sys.all_columns y;
            {read}
            """;
        var scans = (await ActualRelOpsAsync(batch, "JOIN @t b"))
            .Where(op => op.Attribute("PhysicalOp")!.Value.Contains("Scan", StringComparison.Ordinal))
            .Select(op => double.Parse(op.Attribute("EstimateRows")!.Value, CultureInfo.InvariantCulture))
            .ToList();
        Assert.NotEmpty(scans);
        Assert.Equal(fixedOneRow, scans.TrueForAll(estimate => estimate <= 1));

        var (result, catalog) = Parse(batch);
        catalog.CompatibilityLevel = databaseLevel;
        Assert.Equal(
            fixedOneRow,
            QueryAntiPatternScanner.Scan(result, catalog).Any(f => f.Kind == QueryAntiPatternFindingKind.TableVariableLowCompatEstimate));
    }

    [Theory]
    [InlineData("JSON_ARRAYAGG(v)", "[1,2,3]")]
    [InlineData("JSON_OBJECTAGG(CAST(v AS VARCHAR(5)):v)", "{\"1\":1,\"2\":2,\"3\":3}")]
    [InlineData("CAST(APPROX_PERCENTILE_DISC(0.5) WITHIN GROUP (ORDER BY v) AS VARCHAR(20))", "2")]
    [InlineData("CAST(APPROX_PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY v) AS VARCHAR(20))", "2")]
    [InlineData("CAST(PRODUCT(v) AS VARCHAR(20))", "6")]
    [Trait("Rule", FindingRuleIds.TriggerCorrectnessMultiRowUnsafeSingleRowAssignmentRuleId)]
    [Trait("Rule", FindingRuleIds.TriggerCorrectnessMultiRowUnsafeKeyedDmlRuleId)]
    public async Task NewAggregates_ReduceEveryRow_AndATriggerUsingThemIsNotMultiRowUnsafe(string aggregate, string expected)
    {
        var value = await ScalarAsync<string>($"DECLARE @r VARCHAR(100); SELECT @r = {aggregate} FROM (VALUES (1),(2),(3)) t(v); SELECT @r;");
        Assert.Equal(expected, value);

        var (result, catalog) = Parse(
            $"CREATE TABLE dbo.T (v INT NOT NULL);\nGO\nCREATE TRIGGER dbo.tr ON dbo.T AFTER INSERT AS BEGIN DECLARE @r VARCHAR(100); SELECT @r = {aggregate} FROM inserted; END;");
        Assert.DoesNotContain(
            TriggerCorrectnessScanner.Scan(result, catalog),
            f => f.Kind == TriggerCorrectnessFindingKind.MultiRowUnsafeSingleRowAssignment);
    }

    [Theory]
    [InlineData("VARCHAR(10)", "1", true)]
    [InlineData("VARCHAR(10)", "'1'", false)]
    [InlineData("INT", "1", false)]
    [Trait("Rule", FindingRuleIds.SelfReferencingDmlRuleId)]
    public async Task KeyEqualityPin_OnlyAvoidsTheSpoolWhenTheKeyColumnNeedsNoConversion(string keyType, string literal, bool spools)
    {
        var ddl = $"CREATE TABLE dbo.K (k {keyType} COLLATE_PLACEHOLDER NOT NULL PRIMARY KEY, v INT NOT NULL);"
            .Replace(" COLLATE_PLACEHOLDER", keyType.StartsWith("VARCHAR", StringComparison.Ordinal) ? " COLLATE Latin1_General_100_CI_AS" : string.Empty, StringComparison.Ordinal);
        var update = $"UPDATE dbo.K SET v = v + 1 WHERE k = {literal} AND v > (SELECT MIN(v) FROM dbo.K);";
        await ExecuteAsync(ddl);
        await ExecuteAsync("INSERT dbo.K VALUES (1, 1), (2, 2);");
        var plan = await PlanInSessionAsync(string.Empty, update);
        Assert.Equal(spools, plan.Descendants().Any(e => e.Name.LocalName == "RelOp" && (string?)e.Attribute("PhysicalOp") == "Table Spool"));

        var (result, catalog) = Parse($"{ddl}\nGO\n{update}");
        var views = ViewDefinitionExtractor.Extract([result], catalog.DefaultCollation, catalog.TypeAliases, ledger: null).Views;
        Assert.Equal(spools, SelfReferencingDmlScanner.Scan(result, catalog, ViewExpansionMap.Build(views, catalog)).Count > 0);
    }

    [Theory]
    [InlineData("DATETIMEOFFSET(0)", "'2020-01-01 00:00:00 +00:00'", "'2020-01-01 01:00:00 +01:00'", true)]
    [InlineData("SQL_VARIANT", "CAST(1 AS INT)", "CAST(1 AS BIGINT)", true)]
    [Trait("Rule", FindingRuleIds.NonUniqueUpdateSourceRuleId)]
    public async Task EqualityPinOnAColumn_IsOnlySafeWhenEqualValuesAreStoredIdentically(string type, string first, string second, bool differs)
    {
        var distinctStored = await ScalarAsync<int>($"""
            DECLARE @s TABLE (d {type});
            INSERT @s VALUES ({first});
            INSERT @s VALUES ({second});
            SELECT COUNT(DISTINCT CONVERT(VARCHAR(60), d, 121) + ISNULL(CAST(SQL_VARIANT_PROPERTY(d, 'BaseType') AS VARCHAR(30)), ''))
            FROM @s WHERE d = (SELECT TOP (1) d FROM @s);
            """);
        Assert.Equal(differs, distinctStored > 1);

        var (result, catalog) = Parse(
            $"CREATE TABLE dbo.T (k INT NOT NULL PRIMARY KEY, c {type});\nCREATE TABLE dbo.S (k INT NOT NULL, d {type});\nGO\n"
            + $"DECLARE @x {type}; UPDATE t SET t.c = s.d FROM dbo.T t JOIN dbo.S s ON s.k = t.k AND s.d = @x;");
        Assert.Equal(differs, NonUniqueUpdateSourceScanner.Scan(result, catalog).Count > 0);
    }

    [Fact]
    [Trait("Rule", FindingRuleIds.ControlFlowRiskNonDeterministicCaseInputRuleId)]
    public async Task NonDeterministicCaseInput_StillHitsWhenBranches_SoTheMessageDoesNotClaimTheyAreUnreachable()
    {
        const string sample = """
            SELECT COUNT(CASE WHEN b = 'zero' THEN 1 END), COUNT(CASE WHEN b = 'else' THEN 1 END)
            FROM (SELECT CASE ABS(CHECKSUM(NEWID())) % 3 WHEN 0 THEN 'zero' WHEN 1 THEN 'one' WHEN 2 THEN 'two' ELSE 'else' END AS b
                  FROM sys.all_columns) d;
            """;
        var counts = (await RowsAsync(sample))[0];
        Assert.True((int)counts[0]! > 0);
        Assert.True((int)counts[1]! > 0);

        var (result, catalog) = Parse("SELECT CASE NEWID() WHEN '00000000-0000-0000-0000-000000000000' THEN 1 WHEN '11111111-1111-1111-1111-111111111111' THEN 2 ELSE 3 END;");
        var finding = Assert.Single(
            ControlFlowRiskScanner.Scan(result, catalog),
            f => f.Kind == ControlFlowRiskFindingKind.NonDeterministicCaseInput);
        Assert.DoesNotContain("unreachable", finding.DetailText, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Rule", FindingRuleIds.WindowFrameImplicitDefaultRangeFrameRuleId)]
    public async Task ExplicitRowsFrame_DiffersFromTheDefaultRangeFrameOnlyForTiedKeys()
    {
        var rows = await RowsAsync("""
            SELECT g, SUM(a) OVER (PARTITION BY g ORDER BY d) AS dflt,
                   SUM(a) OVER (PARTITION BY g ORDER BY d ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS rws
            FROM (VALUES (1,1,10),(1,1,20),(2,1,7),(2,2,8)) v(g,d,a);
            """);
        Assert.Contains(rows, r => (int)r[0]! == 1 && (int)r[1]! != (int)r[2]!);
        Assert.All(rows.Where(r => (int)r[0]! == 2), r => Assert.Equal(r[1], r[2]));

        var rationale = SilentScan.Core.Reporting.RuleCatalog.BaseRules
            .Single(r => r.Id == "silentscan/window-frame/implicit-default-range");
        Assert.Contains("unique", rationale.FixGuidance, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("CHARINDEX('50%', v) = 1", "v LIKE '50%%'", "v LIKE '50[%]%'")]
    [InlineData("LEFT(v, 3) = 'ab '", "v LIKE 'ab %'", "RTRIM(LEFT(v, 3)) = 'ab'")]
    [Trait("Rule", FindingRuleIds.Tier1CharindexOrLeftOnColumnRuleId)]
    public async Task NaiveLikeRewrite_ChangesTheRows_AndTheFindingDoesNotOfferIt(string original, string naive, string correct)
    {
        const string rows = "(VALUES ('50%'),('50x'),('50'),('ab'),('abc'),('ab  x')) t(v)";
        var originalCount = await ScalarAsync<int>($"SELECT COUNT(*) FROM {rows} WHERE {original};");
        Assert.NotEqual(originalCount, await ScalarAsync<int>($"SELECT COUNT(*) FROM {rows} WHERE {naive};"));
        Assert.Equal(originalCount, await ScalarAsync<int>($"SELECT COUNT(*) FROM {rows} WHERE {correct};"));

        var (result, _) = Parse($"CREATE TABLE dbo.T (v VARCHAR(20) NOT NULL INDEX IX_v);\nGO\nSELECT v FROM dbo.T WHERE {original};");
        var finding = Assert.Single(
            NonSargablePredicateScanner.Scan(result), f => f.Kind == SargabilityFindingKind.CharindexOrLeftOnColumn);
        Assert.DoesNotContain("rewritable to col LIKE 'x%', which", finding.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("^v12", true)]
    [InlineData("^v12[0-9]", true)]
    [InlineData("^v12[0-9]$", true)]
    [InlineData("^v12+", true)]
    [InlineData("^v12*x", true)]
    [InlineData("^vv?", true)]
    [InlineData("^v{2}", true)]
    [InlineData("^v\\.", true)]
    [InlineData("^[ab]*", true)]
    [InlineData("^v12[^a]", true)]
    [InlineData("v12", false)]
    [InlineData("^v*", false)]
    [InlineData("^v?", false)]
    [InlineData("^v{0,}", false)]
    [InlineData("^v12.", false)]
    [InlineData("^v12.*", false)]
    [InlineData("^v12$", false)]
    [InlineData("^v1\\d", false)]
    [InlineData("^v12|x", false)]
    [InlineData("^(v12)", false)]
    [InlineData("^[^a]", false)]
    [InlineData("^v12[[:digit:]]", false)]
    [InlineData("^v-1", false)]
    [Trait("Rule", FindingRuleIds.Tier1RegexpPatternNoLiteralPrefixRuleId)]
    public async Task RegexpLike_SeeksOnlyForPatternsWithADerivablePrefix(string pattern, bool seeks)
    {
        const string ddl = "CREATE TABLE dbo.R (s VARCHAR(50) COLLATE Latin1_General_100_CS_AS NOT NULL INDEX IX_R CLUSTERED);";
        var probe = $"SELECT s FROM dbo.R WHERE REGEXP_LIKE(s, '{pattern}');";
        await ExecuteAsync("ALTER DATABASE CURRENT SET COMPATIBILITY_LEVEL = 170;");
        await ExecuteAsync(ddl);
        await ExecuteAsync("INSERT dbo.R SELECT TOP (3000) CONCAT('v', ROW_NUMBER() OVER (ORDER BY (SELECT 1))) FROM sys.all_columns a CROSS JOIN sys.all_columns b;");
        var plan = await PlanInSessionAsync(string.Empty, probe);
        Assert.Equal(
            seeks,
            plan.Descendants().Any(e => e.Name.LocalName == "RelOp" && (string?)e.Attribute("PhysicalOp") == "Clustered Index Seek"));

        var (result, _) = Parse($"{ddl}\nGO\n{probe}");
        Assert.Equal(
            !seeks,
            NonSargablePredicateScanner.Scan(result).Any(f => f.Kind == SargabilityFindingKind.RegexpPatternNoLiteralPrefix));
    }
}
