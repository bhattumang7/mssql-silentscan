using System.Globalization;
using System.Xml.Linq;
using SilentScan.Tests.Support;
using SilentScan.Verify;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class StatementContextEngineFactOracleTests : OracleTestFixture
{
    protected override SqlServerOptions Options => SqlServerOptions.LocalDockerLatest;

    protected override string DatabaseNameSeed => nameof(StatementContextEngineFactOracleTests);

    protected override string Ddl => """
        ALTER DATABASE CURRENT SET AUTO_UPDATE_STATISTICS OFF;
        ALTER DATABASE CURRENT SET AUTO_CREATE_STATISTICS OFF;
        GO
        CREATE TABLE dbo.Src (Id INT NOT NULL PRIMARY KEY, V INT NOT NULL);
        INSERT INTO dbo.Src SELECT value, value FROM GENERATE_SERIES(1, 5000);
        CREATE TABLE dbo.Sink (Id INT NOT NULL);
        GO
        CREATE FUNCTION dbo.Ms() RETURNS @r TABLE (Id INT, V INT) AS BEGIN INSERT @r SELECT Id, V FROM dbo.Src; RETURN; END;
        GO
        CREATE VIEW dbo.VMs AS SELECT s.Id, f.V FROM dbo.Src AS s JOIN dbo.Ms() AS f ON f.Id = s.Id;
        GO
        CREATE TABLE dbo.Skewed (Id INT IDENTITY PRIMARY KEY, K INT NOT NULL, Pad CHAR(100) NOT NULL DEFAULT 'x');
        INSERT INTO dbo.Skewed (K) SELECT 1 FROM GENERATE_SERIES(1, 10000);
        INSERT INTO dbo.Skewed (K) VALUES (2);
        CREATE INDEX IX_Skewed ON dbo.Skewed (K);
        UPDATE STATISTICS dbo.Skewed WITH FULLSCAN;
        GO
        CREATE PROCEDURE dbo.InsertAfterReassignment @k INT AS BEGIN SET @k = 1; INSERT dbo.Sink SELECT Id FROM dbo.Skewed WHERE K = @k AND Pad <> ''; END;
        GO
        CREATE PROCEDURE dbo.RecompiledAfterReassignment @k INT AS
        BEGIN
            SET @k = 1;
            INSERT dbo.Skewed (K) SELECT 1 FROM GENERATE_SERIES(1, 5000);
            UPDATE STATISTICS dbo.Skewed WITH FULLSCAN;
            DECLARE @c INT;
            SELECT @c = COUNT(Pad) FROM dbo.Skewed WHERE K = @k AND Pad <> '';
        END;
        GO
        CREATE TABLE dbo.Filtered (C INT NOT NULL, D INT NOT NULL, Pad CHAR(50) NOT NULL DEFAULT 'x');
        INSERT INTO dbo.Filtered (C, D) SELECT CASE WHEN value <= 7000 THEN 11 ELSE value END, value % 10 FROM GENERATE_SERIES(1, 10000);
        CREATE STATISTICS SF ON dbo.Filtered (C) WHERE D = 1 WITH FULLSCAN;
        GO
        CREATE TABLE dbo.Stale (C INT NOT NULL);
        INSERT INTO dbo.Stale SELECT value FROM GENERATE_SERIES(1, 1000);
        CREATE STATISTICS SC ON dbo.Stale (C) WITH FULLSCAN, NORECOMPUTE;
        GO
        CREATE TABLE dbo.Masked (Id INT NOT NULL, Amount INT MASKED WITH (FUNCTION = 'default()') NOT NULL);
        INSERT INTO dbo.Masked VALUES (1, 10);
        GO
        CREATE VIEW dbo.VMasked AS SELECT Id, Amount FROM dbo.Masked;
        GO
        CREATE USER Reader WITHOUT LOGIN;
        GRANT SELECT ON dbo.Masked TO Reader;
        GRANT SELECT ON dbo.VMasked TO Reader;
        GO
        CREATE TABLE dbo.Outer1 (K INT NOT NULL, F INT NOT NULL);
        CREATE TABLE dbo.Inner1 (K INT NULL);
        INSERT INTO dbo.Outer1 VALUES (1, 0), (2, 0), (3, 1);
        INSERT INTO dbo.Inner1 VALUES (2), (NULL);
        GO
        CREATE TABLE dbo.Computed (Id INT IDENTITY PRIMARY KEY, V INT NOT NULL, Twice AS (V * 2) PERSISTED, Pad CHAR(200) NOT NULL DEFAULT 'x');
        INSERT INTO dbo.Computed (V) SELECT value FROM GENERATE_SERIES(1, 20000);
        CREATE INDEX IX_Twice ON dbo.Computed (Twice);
        GO
        CREATE TABLE dbo.Parent (Id INT NOT NULL PRIMARY KEY, Name VARCHAR(10) NOT NULL);
        CREATE TABLE dbo.Child (Id INT NOT NULL PRIMARY KEY, ParentId INT NOT NULL);
        INSERT INTO dbo.Parent VALUES (1, 'a'), (2, 'b');
        INSERT INTO dbo.Child VALUES (10, 1), (11, 1), (12, 1), (20, 2);
        GO
        CREATE TABLE dbo.Coupon (Code VARCHAR(20) NOT NULL);
        CREATE UNIQUE INDEX UX_Coupon ON dbo.Coupon (Code) WITH (IGNORE_DUP_KEY = ON);
        GO
        CREATE TABLE dbo.Orders (Id INT NOT NULL PRIMARY KEY, Status INT NOT NULL);
        CREATE TABLE dbo.Landed (Id INT NOT NULL);
        CREATE TABLE dbo.Ref1 (Id INT NOT NULL, Status INT NOT NULL);
        INSERT INTO dbo.Orders VALUES (1, 5);
        INSERT INTO dbo.Ref1 VALUES (1, 5);
        GO
        CREATE VIEW dbo.VLanded AS SELECT Id FROM dbo.Landed;
        GO
        CREATE TRIGGER dbo.TrLanded ON dbo.VLanded INSTEAD OF INSERT AS
        BEGIN
            INSERT dbo.Landed SELECT TOP (1) Id FROM inserted;
            RAISERROR('kept going', 10, 1);
        END;
        GO
        CREATE TRIGGER dbo.TrOrders ON dbo.Orders AFTER UPDATE AS
        BEGIN
            IF UPDATE(Status) INSERT dbo.Landed SELECT i.Id FROM inserted AS i JOIN dbo.Ref1 AS a ON a.Id = i.Id JOIN dbo.Ref1 AS b ON a.Status = b.Status;
        END;
        GO
        """;

    private static double Number(XAttribute? attribute) => double.Parse(attribute!.Value, CultureInfo.InvariantCulture);

    private async Task<XDocument> ActualPlanAsync(string probe) => XDocument.Parse(await CaptureActualPlanAsync(probe));

    [Theory]
    [Trait("Rule", FindingRuleIds.TvfFenceNestedUnderViewOrTvfRuleId)]
    [InlineData("SELECT Id FROM dbo.VMs;", 5000)]
    [InlineData("INSERT dbo.Sink SELECT Id FROM dbo.VMs;", 100)]
    [InlineData("IF EXISTS (SELECT 1 FROM dbo.VMs WHERE V < 0) SELECT 1;", 100)]
    [InlineData("SELECT Id FROM dbo.VMs OPTION (USE HINT('DISABLE_INTERLEAVED_EXECUTION_TVF'));", 100)]
    [InlineData("SELECT Id FROM dbo.VMs OPTION (QUERYTRACEON 11005);", 100)]
    public async Task OnlyAReadOnlySelectGetsTheRealRowCount(string statement, double estimate)
    {
        var plan = await ActualPlanAsync(statement);

        Assert.Equal(estimate, ShowPlan.EstimatedRows(ShowPlan.Ops(plan, "Table-valued function").First()));
    }

    [Theory]
    [Trait("Rule", FindingRuleIds.QueryAntiPatternTableVariableLowCompatEstimateRuleId)]
    [InlineData("", false)]
    [InlineData(" OPTION (USE HINT('DISABLE_DEFERRED_COMPILATION_TV'))", true)]
    public async Task TheHintFixesTheTableVariableEstimateAtOneRow(string hint, bool oneRow)
    {
        var plan = await ActualPlanAsync($"DECLARE @t TABLE (Id INT); INSERT @t SELECT TOP (1000) Id FROM dbo.Src; SELECT Id FROM @t{hint};");

        Assert.Equal(oneRow, ShowPlan.EstimatedRows(ShowPlan.Ops(plan, "Table Scan").Last()) <= 1);
    }

    [Fact]
    [Trait("Rule", FindingRuleIds.ParameterReassignmentPredicateRuleId)]
    public async Task AnInsertSelectAfterReassignmentKeepsTheCallersEstimate()
    {
        var plan = await ActualPlanAsync("EXEC dbo.InsertAfterReassignment 2;");

        Assert.True(ShowPlan.EstimatedRows(ShowPlan.Ops(plan, "Index Seek").Single()) < 100);
    }

    [Fact]
    [Trait("Rule", FindingRuleIds.ParameterReassignmentPredicateRuleId)]
    public async Task AStatementRecompileUsesTheReassignedValue()
    {
        var plan = await CaptureActualPlanAsync("EXEC dbo.RecompiledAfterReassignment 2;");

        Assert.Contains("ParameterCompiledValue=\"(1)\"", plan, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Rule", FindingRuleIds.MissingStatisticsRuleId)]
    [InlineData("C = 11", false)]
    [InlineData("C = 11 AND D = 1", true)]
    public async Task AFilteredStatisticNeedsItsFilterInTheQuery(string predicate, bool used)
    {
        var plan = await PlanInSessionAsync(string.Empty, $"SELECT Pad FROM dbo.Filtered WHERE {predicate} OPTION (RECOMPILE);");

        Assert.Equal(used, plan.Descendants(ShowPlan.Ns + "StatisticsInfo").Any(s => ((string?)s.Attribute("Statistics")) == "[SF]"));
    }

    [Fact]
    [Trait("Rule", FindingRuleIds.DatabaseConfigurationAutoUpdateStatisticsOffRuleId)]
    [Trait("Rule", FindingRuleIds.IndexDesignNoRecomputeStatisticsRuleId)]
    public async Task OnlyAManualRefreshUpdatesTheStatistic()
    {
        const string rows = "SELECT CAST(p.rows AS INT) FROM sys.stats s CROSS APPLY sys.dm_db_stats_properties(s.object_id, s.stats_id) p WHERE s.object_id = OBJECT_ID('dbo.Stale') AND s.name = 'SC';";
        await ExecuteAsync("INSERT INTO dbo.Stale SELECT value FROM GENERATE_SERIES(1001, 6000); SELECT COUNT(*) FROM dbo.Stale WHERE C > 5 OPTION (RECOMPILE);");
        Assert.Equal(1000, await ScalarAsync<int>(rows));

        await ExecuteAsync("EXEC sp_updatestats;");

        Assert.Equal(6000, await ScalarAsync<int>(rows));
    }

    [Theory]
    [Trait("Rule", FindingRuleIds.DynamicDataMaskingComputedExpressionCollapseRuleId)]
    [InlineData("dbo.Masked")]
    [InlineData("dbo.VMasked")]
    public async Task AnExpressionOverAMaskedColumnCollapsesThroughAViewToo(string source)
    {
        Assert.Equal(0, await ScalarAsync<int>($"EXECUTE AS USER = 'Reader'; SELECT Amount + 1000 FROM {source}; REVERT;"));
    }

    [Theory]
    [Trait("Rule", FindingRuleIds.StringConcatNullRuleId)]
    [InlineData("'x' + ISNULL(a, b)", 1)]
    [InlineData("'x' + COALESCE(a, b)", 1)]
    [InlineData("'x' + ISNULL(a, '')", 0)]
    public async Task ANullableFallbackStillLosesTheString(string expression, int nulls)
    {
        Assert.Equal(nulls, await ScalarAsync<int>(
            $"SELECT COUNT(*) FROM (VALUES (CAST(NULL AS VARCHAR(10)), CAST(NULL AS VARCHAR(10)))) AS t(a, b) WHERE {expression} IS NULL;"));
    }

    [Theory]
    [Trait("Rule", FindingRuleIds.NotInNullableSubqueryRuleId)]
    [InlineData("NOT (K IN (SELECT K FROM dbo.Inner1))", 0)]
    [InlineData("K <> ALL (SELECT K FROM dbo.Inner1)", 0)]
    [InlineData("NOT (K = ANY (SELECT K FROM dbo.Inner1))", 0)]
    [InlineData("F = 1 OR K NOT IN (SELECT K FROM dbo.Inner1)", 1)]
    [InlineData("NOT (K NOT IN (SELECT K FROM dbo.Inner1))", 1)]
    [InlineData("K NOT IN (SELECT K FROM dbo.Inner1 WHERE K IS NOT NULL)", 2)]
    public async Task EveryFormOfNotInLosesRowsToOneNull(string predicate, int rows)
    {
        Assert.Equal(rows, await ScalarAsync<int>($"SELECT COUNT(*) FROM dbo.Outer1 WHERE {predicate};"));
    }

    [Theory]
    [Trait("Rule", FindingRuleIds.SetOptionAnsiWarningsOffBlocksIndexedFeatureRuleId)]
    [Trait("Rule", FindingRuleIds.SetOptionConcatNullYieldsNullOffBlocksIndexedFeatureRuleId)]
    [Trait("Rule", FindingRuleIds.SetOptionAnsiPaddingOffBlocksIndexedFeatureRuleId)]
    [Trait("Rule", FindingRuleIds.SetOptionNumericRoundabortOnBlocksIndexedFeatureRuleId)]
    [Trait("Rule", FindingRuleIds.SetOptionAnsiNullsOffBlocksIndexedFeatureRuleId)]
    [Trait("Rule", FindingRuleIds.SetOptionQuotedIdentifierOffBlocksIndexedFeatureRuleId)]
    [InlineData("", true)]
    [InlineData("SET ANSI_WARNINGS OFF;", false)]
    [InlineData("SET CONCAT_NULL_YIELDS_NULL OFF;", false)]
    [InlineData("SET ANSI_PADDING OFF;", false)]
    [InlineData("SET NUMERIC_ROUNDABORT ON;", false)]
    [InlineData("SET ANSI_NULLS OFF;", false)]
    [InlineData("SET QUOTED_IDENTIFIER OFF;", false)]
    public async Task AnIndexedComputedColumnIsLostUnderTheBlockingOptions(string option, bool used)
    {
        var plan = await PlanInSessionAsync(option, "SELECT Id FROM dbo.Computed WHERE Twice = 10 OPTION (RECOMPILE);");

        Assert.Equal(used, ShowPlan.IndexNames(plan).Any(name => name.Contains("IX_Twice", StringComparison.Ordinal)));
    }

    [Theory]
    [Trait("Rule", FindingRuleIds.QueryAntiPatternDistinctMaskingJoinFanoutRuleId)]
    [InlineData("p.Id, p.Name", 2)]
    [InlineData("c.Id, p.Name", 4)]
    public async Task DistinctHidesAFanoutOnTheFirstTable(string projection, int rows)
    {
        Assert.Equal(rows, await ScalarAsync<int>(
            $"SELECT COUNT(*) FROM (SELECT DISTINCT {projection} FROM dbo.Child AS c JOIN dbo.Parent AS p ON p.Id = c.ParentId) AS d;"));
    }

    [Theory]
    [Trait("Rule", FindingRuleIds.QueryAntiPatternCountStarVariableExistenceCheckRuleId)]
    [InlineData("DECLARE @n INT; SET @n = (SELECT COUNT(*) FROM dbo.Skewed WHERE K = 1 AND Pad <> '');", true)]
    [InlineData("IF EXISTS (SELECT 1 FROM dbo.Skewed WHERE K = 1 AND Pad <> '') SELECT 1;", false)]
    public async Task ACountAssignedWithSetReadsEveryMatch(string statement, bool readsEverything)
    {
        var plan = await ActualPlanAsync(statement);

        var read = plan.Descendants(ShowPlan.Ns + "RunTimeCountersPerThread").Max(c => Number(c.Attribute("ActualRows")));

        Assert.Equal(readsEverything, read >= 10000);
    }

    [Fact]
    [Trait("Rule", FindingRuleIds.QueryAntiPatternMultiRowInsertIgnoreDupKeyDropRuleId)]
    public async Task ADroppedDuplicateIsNotCaughtByTryCatch()
    {
        Assert.Equal(1, await ScalarAsync<int>(
            "BEGIN TRY INSERT dbo.Coupon VALUES ('a'), ('a'); SELECT COUNT(*) FROM dbo.Coupon; END TRY BEGIN CATCH SELECT -1; END CATCH;"));
    }

    [Fact]
    [Trait("Rule", FindingRuleIds.TriggerCorrectnessInsteadOfInsertFilteredNoRejectPathRuleId)]
    public async Task ATopReinsertWithAnInformationalRaiserrorDropsRowsWithoutAnError()
    {
        await ExecuteAsync("INSERT dbo.VLanded VALUES (101), (102), (103);");

        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Landed WHERE Id > 100;"));
    }

    [Fact]
    [Trait("Rule", FindingRuleIds.TriggerCorrectnessUpdateFunctionWithoutValueComparisonRuleId)]
    public async Task ABaseTableComparisonDoesNotStopANoOpUpdateFromActing()
    {
        await ExecuteAsync("UPDATE dbo.Orders SET Status = Status;");

        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Landed WHERE Id = 1;"));
    }
}
