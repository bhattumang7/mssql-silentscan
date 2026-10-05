using System.Xml.Linq;
using Microsoft.Data.SqlClient;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", "silentscan/forced-serial/table-variable-modification")]
public sealed class TableVariableModificationOracleTests : OracleTestFixture
{
    private static readonly string[] NonScanningOperators = ["Table Insert", "Constant Scan", "Compute Scalar", "Parameter Table Scan"];

    protected override string DatabaseNameSeed => nameof(TableVariableModificationOracleTests);

    protected override string Ddl => """
        CREATE TABLE dbo.BigTable (Id INT NOT NULL, Grp INT NOT NULL, Val VARCHAR(100) NOT NULL);
        GO
        CREATE TABLE dbo.Audit (Id INT NOT NULL);
        GO
        CREATE PROCEDURE dbo.ProbeProc AS SELECT 1 AS Id;
        GO
        """;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();

        await using var connection = new SqlConnection(Options.BuildConnectionString(DatabaseName));
        await connection.OpenAsync();

        await using var seedCommand = new SqlCommand(
            """
            INSERT INTO dbo.BigTable (Id, Grp, Val)
            SELECT TOP (200000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)),
                   ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) % 100, REPLICATE('x', 50)
            FROM sys.all_objects a CROSS JOIN sys.all_objects b;
            UPDATE STATISTICS dbo.BigTable WITH FULLSCAN;
            """, connection);
        await seedCommand.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task InsertIntoTableVariable_ForcesSerial()
    {
        var planXml = await CaptureActualPlanAsync(
            "DECLARE @t TABLE (Grp INT, Cnt INT); INSERT INTO @t (Grp, Cnt) SELECT Grp, COUNT(*) FROM dbo.BigTable GROUP BY Grp OPTION (MAXDOP 0);");

        Assert.Contains("NonParallelPlanReason=\"TableVariableTransactionsDoNotSupportParallelNestedTransaction\"", planXml);
    }

    [Fact]
    public async Task OutputIntoTableVariable_ForcesSerial()
    {
        var planXml = await CaptureActualPlanAsync(
            "DECLARE @out TABLE (Id INT); DELETE FROM dbo.BigTable OUTPUT deleted.Id INTO @out WHERE Grp = -1 OPTION (MAXDOP 0);");

        Assert.Contains("NonParallelPlanReason=\"TableVariableTransactionsDoNotSupportParallelNestedTransaction\"", planXml);
    }

    [Fact]
    public async Task ReadOnlyReferenceToTableVariable_NeverBlocksParallelism()
    {
        var planXml = await CaptureActualPlanAsync(
            """
            DECLARE @t TABLE (Grp INT);
            INSERT INTO @t (Grp) VALUES (1);
            SELECT b.Id FROM dbo.BigTable b JOIN @t t ON b.Grp = t.Grp OPTION (MAXDOP 0);
            """);

        Assert.DoesNotContain("NonParallelPlanReason=\"TableVariableTransactionsDoNotSupportParallelNestedTransaction\"", planXml);
    }

    [Fact]
    public async Task OutputIntoRealTable_NeverBlocksParallelism()
    {
        var planXml = await CaptureActualPlanAsync(
            "DELETE FROM dbo.BigTable OUTPUT deleted.Id INTO dbo.Audit WHERE Grp = -1 OPTION (MAXDOP 0);");

        Assert.DoesNotContain("NonParallelPlanReason=\"TableVariableTransactionsDoNotSupportParallelNestedTransaction\"", planXml);
    }

    [Fact]
    public async Task InsertIntoTempTable_FromLargeSource_GetsParallelPlan()
    {
        var planXml = await CaptureActualPlanAsync(
            "CREATE TABLE #t (Grp INT, Cnt INT); INSERT INTO #t (Grp, Cnt) SELECT Grp, COUNT(*) FROM dbo.BigTable GROUP BY Grp OPTION (USE HINT('ENABLE_PARALLEL_PLAN_PREFERENCE'));");

        Assert.Contains("PhysicalOp=\"Parallelism\"", planXml);
    }

    [Fact]
    public async Task InsertIntoTableVariable_EvenWhenParallelPlanIsPreferred_StaysSerial()
    {
        var planXml = await CaptureActualPlanAsync(
            "DECLARE @t TABLE (Grp INT, Cnt INT); INSERT INTO @t (Grp, Cnt) SELECT Grp, COUNT(*) FROM dbo.BigTable GROUP BY Grp OPTION (USE HINT('ENABLE_PARALLEL_PLAN_PREFERENCE'));");

        Assert.DoesNotContain("PhysicalOp=\"Parallelism\"", planXml);
        Assert.Contains("NonParallelPlanReason=\"TableVariableTransactionsDoNotSupportParallelNestedTransaction\"", planXml);
    }

    [Theory]
    [InlineData("INSERT INTO @t (Grp) VALUES (1);")]
    [InlineData("INSERT INTO @t (Grp) VALUES (1), (2), (3);")]
    [InlineData("INSERT INTO @t (Grp) SELECT 1;")]
    [InlineData("INSERT INTO @t (Grp) EXEC dbo.ProbeProc;")]
    public async Task InsertIntoTableVariable_WithoutRowSource_HasNothingToParallelize(string statement)
    {
        var planXml = await CaptureActualPlanAsync($"DECLARE @t TABLE (Grp INT); {statement}");

        var physicalOps = XDocument.Parse(planXml).Descendants()
            .Where(element => element.Name.LocalName == "RelOp")
            .Select(element => (string?)element.Attribute("PhysicalOp"))
            .ToList();

        Assert.NotEmpty(physicalOps);
        Assert.All(physicalOps, op => Assert.Contains(op, NonScanningOperators));
    }

    [Fact]
    public async Task InsertIntoTableVariable_FromRealTable_ScansTheRealTable()
    {
        var planXml = await CaptureActualPlanAsync(
            "DECLARE @t TABLE (Grp INT); INSERT INTO @t (Grp) SELECT Grp FROM dbo.BigTable OPTION (MAXDOP 0);");

        var physicalOps = XDocument.Parse(planXml).Descendants()
            .Where(element => element.Name.LocalName == "RelOp")
            .Select(element => (string?)element.Attribute("PhysicalOp"))
            .ToList();

        Assert.Contains(physicalOps, op => op is "Table Scan" or "Clustered Index Scan" or "Index Scan");
    }
}
