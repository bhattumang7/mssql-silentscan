using System.Globalization;
using System.Xml.Linq;
using SilentScan.Core.Catalog;
using SilentScan.Core.Predicates;
using SilentScan.Verify.Catalog;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class DatabaseScopedGateOracleTests : EngineDivergenceOracleTestBase
{
    protected override string DatabaseNameSeed => nameof(DatabaseScopedGateOracleTests);

    private const string Orders = "CREATE TABLE dbo.Orders (Id INT IDENTITY PRIMARY KEY, CustomerId INT NOT NULL, Pad CHAR(200) NOT NULL DEFAULT 'x', INDEX IX_Customer (CustomerId));";

    private async Task SeedOrdersAsync()
    {
        await ExecuteAsync("ALTER DATABASE CURRENT SET COMPATIBILITY_LEVEL = 170;");
        await ExecuteAsync(Orders);
        await ExecuteAsync("INSERT dbo.Orders (CustomerId) SELECT TOP (200000) CASE WHEN ROW_NUMBER() OVER (ORDER BY (SELECT 1)) <= 199996 THEN 1 ELSE ROW_NUMBER() OVER (ORDER BY (SELECT 1)) END FROM sys.all_columns a CROSS JOIN sys.all_columns b;");
        await ExecuteAsync("UPDATE STATISTICS dbo.Orders WITH FULLSCAN;");
    }

    private Task ConfigureAsync(string name, bool enabled) =>
        ExecuteAsync($"ALTER DATABASE SCOPED CONFIGURATION SET {name} = {(enabled ? "ON" : "OFF")};");

    private async Task<DatabaseCatalog> LiveCatalogAsync() =>
        await new LiveCatalogReader(Options.BuildConnectionString(DatabaseName)).ReadAsync();

    private static (SilentScan.Core.Parsing.SqlParseResult Result, DatabaseCatalog Catalog) ParseGated(string code, DatabaseCatalog live)
    {
        var (result, catalog) = Parse($"{Orders}\nGO\n{code}");
        catalog.CompatibilityLevel = live.CompatibilityLevel;
        foreach (var name in live.DisabledScopedConfigurations)
        {
            catalog.DisabledScopedConfigurations.Add(name);
        }

        return (result, catalog);
    }

    private static double Estimate(XElement relOp) => double.Parse((string)relOp.Attribute("EstimateRows")!, CultureInfo.InvariantCulture);

    [Theory]
    [InlineData(170, true, true, "", true)]
    [InlineData(170, false, true, "", false)]
    [InlineData(170, true, false, "", false)]
    [InlineData(160, true, true, "", false)]
    [InlineData(170, true, true, " OPTION (USE HINT('DISABLE_OPTIONAL_PARAMETER_OPTIMIZATION'))", false)]
    [InlineData(170, true, true, " OPTION (USE HINT('DISABLE_PARAMETER_SNIFFING'))", false)]
    [InlineData(170, true, true, " OPTION (USE HINT('QUERY_OPTIMIZER_COMPATIBILITY_LEVEL_160'))", false)]
    [InlineData(170, true, true, " OPTION (OPTIMIZE FOR UNKNOWN)", false)]
    [InlineData(170, true, true, " OPTION (OPTIMIZE FOR (@c UNKNOWN))", false)]
    [InlineData(170, true, true, " OPTION (OPTIMIZE FOR (@c = NULL))", true)]
    [Trait("Rule", FindingRuleIds.CatchAllPredicateRuleId)]
    public async Task CatchAllPredicate_SeeksUnderOptionalParameterOptimization_AndIsThenNotReported(
        int level, bool optional, bool sniffing, string hint, bool seeks)
    {
        await SeedOrdersAsync();
        await ExecuteAsync($"ALTER DATABASE CURRENT SET COMPATIBILITY_LEVEL = {level};");
        await ConfigureAsync("OPTIONAL_PARAMETER_OPTIMIZATION", optional);
        await ConfigureAsync("PARAMETER_SNIFFING", sniffing);
        var procedure = $"CREATE PROCEDURE dbo.Find @c INT = NULL AS SELECT Pad FROM dbo.Orders WHERE CustomerId = @c OR @c IS NULL{hint};";
        await ExecuteAsync(procedure);

        var operators = await ActualRelOpsAsync("EXEC dbo.Find @c = 199999;", "FROM dbo.Orders");
        Assert.Equal(
            seeks,
            operators.Any(o => (string?)o.Attribute("PhysicalOp") == "Index Seek"));

        var (result, catalog) = ParseGated(procedure, await LiveCatalogAsync());
        Assert.Equal(!seeks, CatchAllPredicateScanner.Scan(result, catalog).Count > 0);
    }

    private const string Filter = "FROM dbo.Orders WHERE CustomerId = @c OR @c IS NULL";

    [Theory]
    [InlineData("SELECT Pad " + Filter + ";", true)]
    [InlineData("SELECT CustomerId INTO #copy " + Filter + ";", true)]
    [InlineData("DECLARE @n INT; SELECT @n = COUNT(Pad) " + Filter + ";", false)]
    [InlineData("DECLARE @n INT; SET @n = (SELECT COUNT(Pad) " + Filter + ");", false)]
    [InlineData("SELECT TOP (0) Id FROM dbo.Orders; IF EXISTS (SELECT 1 " + Filter + ") SELECT 1;", false)]
    [InlineData("DECLARE c CURSOR LOCAL FOR SELECT Pad " + Filter + "; OPEN c; FETCH NEXT FROM c; CLOSE c;", false)]
    [Trait("Rule", FindingRuleIds.CatchAllPredicateRuleId)]
    public async Task CatchAllPredicate_IsRescuedOnlyInStatementsThatTakeAnOptionalParameterPlan(string body, bool seeks)
    {
        await SeedOrdersAsync();
        var procedure = $"CREATE PROCEDURE dbo.Find @c INT = NULL AS BEGIN {body} END";
        await ExecuteAsync(procedure);

        var operators = await ActualRelOpsAsync("EXEC dbo.Find @c = 199999;", "@c IS NULL");
        Assert.Equal(seeks, operators.Any(o => (string?)o.Attribute("PhysicalOp") == "Index Seek"));

        var (result, catalog) = ParseGated(procedure, await LiveCatalogAsync());
        Assert.Equal(!seeks, CatchAllPredicateScanner.Scan(result, catalog).Count > 0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("Rule", FindingRuleIds.CatchAllPredicateRuleId)]
    public async Task CatchAllPredicate_InAnInlineFunction_IsRescuedThroughTheCallingProcedure(bool optional)
    {
        await SeedOrdersAsync();
        await ConfigureAsync("OPTIONAL_PARAMETER_OPTIMIZATION", optional);
        const string function = "CREATE FUNCTION dbo.Matching(@c INT) RETURNS TABLE AS RETURN SELECT Pad " + Filter + ";";
        await ExecuteAsync(function);
        await ExecuteAsync("CREATE PROCEDURE dbo.Find @c INT = NULL AS SELECT Pad FROM dbo.Matching(@c);");

        var operators = await ActualRelOpsAsync("EXEC dbo.Find @c = 199999;", "dbo.Matching");
        Assert.Equal(optional, operators.Any(o => (string?)o.Attribute("PhysicalOp") == "Index Seek"));

        var (result, catalog) = ParseGated(function, await LiveCatalogAsync());
        Assert.Equal(!optional, CatchAllPredicateScanner.Scan(result, catalog).Count > 0);
    }

    [Theory]
    [InlineData("@a = 5", false)]
    [InlineData("", true)]
    [Trait("Rule", FindingRuleIds.CatchAllPredicateRuleId)]
    public async Task CatchAllPredicate_AmongSeveral_SeeksOnlyIfTheFirstCompilationPickedIt(string firstArguments, bool secondSeeks)
    {
        await ExecuteAsync("ALTER DATABASE CURRENT SET COMPATIBILITY_LEVEL = 170;");
        const string table = "CREATE TABLE dbo.Pairs (Id INT IDENTITY PRIMARY KEY, A INT NOT NULL, B INT NOT NULL, Pad CHAR(200) NOT NULL DEFAULT 'x', INDEX IX_A (A), INDEX IX_B (B));";
        const string procedure = "CREATE PROCEDURE dbo.Find @a INT = NULL, @b INT = NULL AS SELECT Pad FROM dbo.Pairs WHERE (A = @a OR @a IS NULL) AND (B = @b OR @b IS NULL);";
        await ExecuteAsync(table);
        await ExecuteAsync("INSERT dbo.Pairs (A, B) SELECT TOP (100000) ROW_NUMBER() OVER (ORDER BY (SELECT 1)), ROW_NUMBER() OVER (ORDER BY (SELECT 1)) FROM sys.all_columns x CROSS JOIN sys.all_columns y;");
        await ExecuteAsync("UPDATE STATISTICS dbo.Pairs WITH FULLSCAN;");
        await ExecuteAsync(procedure);
        await ExecuteAsync($"EXEC dbo.Find {firstArguments};");

        var operators = await ActualRelOpsAsync("EXEC dbo.Find @b = 5;", "FROM dbo.Pairs");
        Assert.Equal(secondSeeks, operators.Any(o => (string?)o.Attribute("PhysicalOp") == "Index Seek"));

        var (result, catalog) = ParseGated($"{table}\nGO\n{procedure}", await LiveCatalogAsync());
        Assert.Empty(CatchAllPredicateScanner.Scan(result, catalog));
    }

    [Theory]
    [InlineData(true, "", true)]
    [InlineData(false, "", false)]
    [InlineData(true, " OPTION (USE HINT('DISABLE_PARAMETER_SNIFFING'))", false)]
    [InlineData(true, " OPTION (OPTIMIZE FOR UNKNOWN)", false)]
    [InlineData(true, " OPTION (OPTIMIZE FOR (@c UNKNOWN))", false)]
    [InlineData(true, " OPTION (OPTIMIZE FOR (@c = 1))", false)]
    [Trait("Rule", FindingRuleIds.ParameterReassignmentPredicateRuleId)]
    public async Task ReassignedParameter_IsEstimatedFromTheStaleValue_OnlyWhileItIsSniffed(bool sniffing, string hint, bool stale)
    {
        var procedure = Reassigning(hint);
        await SeedOrdersAsync();
        await ConfigureAsync("PARAMETER_SNIFFING", sniffing);
        await ConfigureAsync("PARAMETER_SENSITIVE_PLAN_OPTIMIZATION", false);
        await ExecuteAsync(procedure);

        Assert.Equal(stale, await ReassignedAccessEstimateAsync() < 100);

        var (result, catalog) = ParseGated(procedure, await LiveCatalogAsync());
        Assert.Equal(stale, ParameterReassignmentPredicateScanner.Scan(result, catalog).Count > 0);
    }

    [Fact]
    [Trait("Rule", FindingRuleIds.ParameterReassignmentPredicateRuleId)]
    public async Task ReassignedParameter_IsEstimatedFromTheRuntimeValue_WhereAParameterSensitivePlanDispatches()
    {
        await SeedOrdersAsync();
        await ExecuteAsync(Reassigning(string.Empty));

        Assert.True(await ReassignedAccessEstimateAsync() > 100);
    }

    private static string Reassigning(string hint) =>
        $"CREATE PROCEDURE dbo.Re @c INT AS BEGIN SET @c = 1; SELECT COUNT(Pad) FROM dbo.Orders WHERE CustomerId = @c{hint}; END";

    private async Task<double> ReassignedAccessEstimateAsync()
    {
        var operators = await ActualRelOpsAsync("EXEC dbo.Re @c = 199999;", "FROM dbo.Orders");
        return Estimate(operators.First(o => o.Elements()
            .Where(e => e.Name.LocalName == "IndexScan")
            .Elements()
            .Any(e => e.Name.LocalName == "Object" && (string?)e.Attribute("Index") == "[IX_Customer]")));
    }

    [Theory]
    [InlineData(true, "", false)]
    [InlineData(false, "", true)]
    [InlineData(true, " OPTION (USE HINT('DISABLE_DEFERRED_COMPILATION_TV'))", true)]
    [Trait("Rule", FindingRuleIds.QueryAntiPatternTableVariableLowCompatEstimateRuleId)]
    [Trait("Rule", FindingRuleIds.QueryAntiPatternTableVariableStaleEstimateInLoopRuleId)]
    public async Task TableVariableEstimate_IsOneRow_WheneverDeferredCompilationIsOff(bool deferred, string hint, bool oneRow)
    {
        await SeedOrdersAsync();
        await ConfigureAsync("DEFERRED_COMPILATION_TV", deferred);
        var procedure = $"""
            CREATE PROCEDURE dbo.Tv AS
            BEGIN
                DECLARE @t TABLE (a INT NOT NULL);
                DECLARE @i INT = 0;
                INSERT @t SELECT TOP (5000) Id FROM dbo.Orders;
                WHILE @i < 1
                BEGIN
                    INSERT @t VALUES (1);
                    SELECT COUNT(*) FROM @t t JOIN dbo.Orders o ON o.Id = t.a{hint};
                    SET @i += 1;
                END
            END
            """;
        await ExecuteAsync(procedure);

        var operators = await ActualRelOpsAsync("EXEC dbo.Tv;", "SELECT COUNT(*) FROM @t");
        Assert.Equal(oneRow, Estimate(operators.First(o => (string?)o.Attribute("PhysicalOp") == "Table Scan")) <= 1.0);

        var (result, catalog) = ParseGated(procedure, await LiveCatalogAsync());
        var findings = QueryAntiPatternScanner.Scan(result, catalog);
        Assert.Equal(oneRow, findings.Any(f => f.Kind == QueryAntiPatternFindingKind.TableVariableLowCompatEstimate));
        Assert.Equal(!oneRow, findings.Any(f => f.Kind == QueryAntiPatternFindingKind.TableVariableStaleEstimateInLoop));
    }

    [Theory]
    [InlineData(true, true, "", true)]
    [InlineData(false, true, "", false)]
    [InlineData(true, false, "", false)]
    [InlineData(true, true, " OPTION (USE HINT('DISABLE_PARAMETER_SNIFFING'))", false)]
    [InlineData(true, true, " OPTION (OPTIMIZE FOR UNKNOWN)", false)]
    [Trait("Rule", FindingRuleIds.QueryAntiPatternTableVariablePspSkipRuleId)]
    public async Task TableValuedParameterCostsAParameterSensitivePlan_OnlyWhereOneWasAvailable(bool sensitive, bool sniffing, string hint, bool available)
    {
        await ExecuteAsync("ALTER DATABASE CURRENT SET QUERY_STORE = ON (OPERATION_MODE = READ_WRITE, QUERY_CAPTURE_MODE = ALL);");
        await SeedOrdersAsync();
        await ConfigureAsync("PARAMETER_SENSITIVE_PLAN_OPTIMIZATION", sensitive);
        await ConfigureAsync("PARAMETER_SNIFFING", sniffing);
        await ExecuteAsync($"CREATE PROCEDURE dbo.Plain @c INT AS SELECT COUNT(*) FROM dbo.Orders WHERE CustomerId = @c{hint};");
        await ExecuteAsync("EXEC dbo.Plain @c = 1;");
        await ExecuteAsync("EXEC dbo.Plain @c = 199999;");
        await ExecuteAsync("EXEC sys.sp_query_store_flush_db;");

        var dispatchers = await ScalarAsync<int>("SELECT COUNT(*) FROM sys.query_store_plan WHERE plan_type_desc = N'Dispatcher Plan';");
        Assert.Equal(available, dispatchers > 0);

        var withTvp = $"""
            CREATE TYPE dbo.IdList AS TABLE (Id INT NOT NULL PRIMARY KEY);
            GO
            CREATE PROCEDURE dbo.ReadsTvp @c INT, @ids dbo.IdList READONLY AS
            SELECT COUNT(*) FROM dbo.Orders o JOIN @ids i ON i.Id = o.Id WHERE o.CustomerId = @c{hint};
            """;
        var (result, catalog) = ParseGated(withTvp, await LiveCatalogAsync());
        Assert.Equal(
            available,
            QueryAntiPatternScanner.Scan(result, catalog).Any(f => f.Kind == QueryAntiPatternFindingKind.TableVariablePspSkip));
    }

    [Theory]
    [InlineData("SELECT COUNT(*) {0};", true)]
    [InlineData("DECLARE @n INT; SELECT @n = COUNT(*) {0};", false)]
    [InlineData("DECLARE @n INT = (SELECT COUNT(*) {0});", false)]
    [InlineData("DECLARE @n INT; SET @n = (SELECT COUNT(*) {0});", false)]
    [InlineData("IF EXISTS (SELECT 1 {0}) SELECT 1;", false)]
    [InlineData("INSERT dbo.Sink SELECT Id {0};", true)]
    [InlineData("DECLARE c CURSOR LOCAL FOR SELECT Id {0}; OPEN c; FETCH NEXT FROM c; CLOSE c;", false)]
    [Trait("Rule", FindingRuleIds.QueryAntiPatternTableVariablePspSkipRuleId)]
    public async Task TableValuedParameterCostsAPlanVariant_OnlyInStatementsThatTakeOne(string shape, bool available)
    {
        await ExecuteAsync("ALTER DATABASE CURRENT SET QUERY_STORE = ON (OPERATION_MODE = READ_WRITE, QUERY_CAPTURE_MODE = ALL);");
        await SeedOrdersAsync();
        await ExecuteAsync("CREATE TABLE dbo.Sink (Id INT NOT NULL);");
        var plain = string.Format(CultureInfo.InvariantCulture, shape, "FROM dbo.Orders WHERE CustomerId = @c");
        await ExecuteAsync($"CREATE PROCEDURE dbo.Plain @c INT AS BEGIN {plain} END");
        await ExecuteAsync("EXEC dbo.Plain @c = 1;");
        await ExecuteAsync("EXEC dbo.Plain @c = 199999;");
        await ExecuteAsync("EXEC sys.sp_query_store_flush_db;");

        var dispatchers = await ScalarAsync<int>("SELECT COUNT(*) FROM sys.query_store_plan WHERE plan_type_desc = N'Dispatcher Plan';");
        Assert.Equal(available, dispatchers > 0);

        var reading = string.Format(CultureInfo.InvariantCulture, shape, "FROM dbo.Orders o JOIN @ids i ON i.Id = o.Id WHERE o.CustomerId = @c")
            .Replace("SELECT Id ", "SELECT o.Id ", StringComparison.Ordinal);
        var withTvp = $"CREATE TABLE dbo.Sink (Id INT NOT NULL);\nGO\nCREATE TYPE dbo.IdList AS TABLE (Id INT NOT NULL PRIMARY KEY);\nGO\nCREATE PROCEDURE dbo.ReadsTvp @c INT, @ids dbo.IdList READONLY AS BEGIN {reading} END";
        var (result, catalog) = ParseGated(withTvp, await LiveCatalogAsync());
        Assert.Equal(available, QueryAntiPatternScanner.Scan(result, catalog).Any(f => f.Kind == QueryAntiPatternFindingKind.TableVariablePspSkip));
    }

    [Theory]
    [InlineData(170, true, "", true)]
    [InlineData(170, false, "", false)]
    [InlineData(170, true, " OPTION (USE HINT('DISABLE_TSQL_SCALAR_UDF_INLINING'))", false)]
    [InlineData(170, true, " OPTION (USE HINT('QUERY_OPTIMIZER_COMPATIBILITY_LEVEL_140'))", false)]
    [InlineData(140, true, "", false)]
    [Trait("Rule", FindingRuleIds.ScalarUdfProjectionInvocationRuleId)]
    public async Task ScalarUdf_IsInlinedOnlyWhileTheDatabaseAndStatementAllowIt(int level, bool inlining, string hint, bool inlined)
    {
        await SeedOrdersAsync();
        await ExecuteAsync($"ALTER DATABASE CURRENT SET COMPATIBILITY_LEVEL = {level};");
        await ConfigureAsync("TSQL_SCALAR_UDF_INLINING", inlining);
        const string function = "CREATE FUNCTION dbo.Twice(@x INT) RETURNS INT AS BEGIN RETURN @x * 2; END";
        await ExecuteAsync(function);

        await ExecuteAsync($"SELECT MAX(dbo.Twice(CustomerId)) FROM dbo.Orders{hint};");
        var calls = await ScalarAsync<long>("SELECT ISNULL(SUM(execution_count), 0) FROM sys.dm_exec_function_stats WHERE database_id = DB_ID();");
        Assert.Equal(inlined, calls == 0);

        var (result, catalog) = ParseGated($"{function}\nGO\nSELECT dbo.Twice(CustomerId) FROM dbo.Orders{hint};", await LiveCatalogAsync());
        var (views, _) = SilentScan.Core.Lineage.ViewDefinitionExtractor.Extract([result], catalog.DefaultCollation, catalog.TypeAliases);
        var finding = Assert.Single(ScalarUdfScanner.Scan(result, catalog, SilentScan.Core.Lineage.ScalarUdfMap.Build(views, catalog)));
        Assert.Equal(inlined, finding.Inlineability != ScalarUdfInlineability.NotInlineable);
    }
}
