using Microsoft.Data.SqlClient;
using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", "silentscan/query/bare-top-no-order-by")]
public sealed class BareTopNoOrderByEngineFactOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(BareTopNoOrderByEngineFactOracleTests);

    protected override string Ddl => """
        CREATE TABLE dbo.Items (Id INT NOT NULL, Val INT NOT NULL);
        GO
        INSERT INTO dbo.Items (Id, Val)
        SELECT TOP (2000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)), ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) % 100
        FROM sys.all_objects a CROSS JOIN sys.all_objects b;
        GO
        """;

    [Fact]
    public async Task SameBareTopQueryText_ReturnsADifferentRowSetOnceASupportingIndexExists()
    {
        const string Query = "SELECT TOP (5) Id FROM dbo.Items WHERE Val = 3;";

        var heapIds = await IdsAsync(Query);

        await ExecuteAsync("""
            CREATE INDEX IX_Items_Val_Id ON dbo.Items(Val, Id DESC);
            UPDATE STATISTICS dbo.Items WITH FULLSCAN;
            """);

        var indexedIds = await IdsAsync(Query);

        Assert.NotEqual(heapIds, indexedIds);
    }

    private async Task<List<int>> IdsAsync(string query)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = new SqlCommand(query, connection) { CommandTimeout = 120 };
        await using var reader = await command.ExecuteReaderAsync();

        var ids = new List<int>();
        while (await reader.ReadAsync())
        {
            ids.Add(reader.GetInt32(0));
        }

        return ids;
    }
}

[Trait("Category", "Oracle")]
[Trait("Rule", "silentscan/view/order-by-not-guaranteed")]
public sealed class ViewOrderingNotGuaranteedToConsumerEngineFactOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(ViewOrderingNotGuaranteedToConsumerEngineFactOracleTests);

    protected override string Ddl => """
        CREATE TABLE dbo.BranchA (Id INT NOT NULL, Amt INT NOT NULL);
        CREATE TABLE dbo.BranchB (Id INT NOT NULL, Amt INT NOT NULL);
        GO
        INSERT INTO dbo.BranchA VALUES (1,10),(2,10),(3,10);
        INSERT INTO dbo.BranchB VALUES (4,10),(5,10),(6,10);
        GO
        CREATE VIEW dbo.V_UnionOrdered AS
        SELECT Id, Amt FROM dbo.BranchA
        UNION ALL
        SELECT Id, Amt FROM dbo.BranchB
        ORDER BY Amt DESC
        OFFSET 0 ROWS FETCH NEXT 100 ROWS ONLY;
        GO
        """;

    [Fact]
    public async Task ConsumerSelectWithNoOwnOrderBy_GetsADifferentRowOrderFromTheSameView_OnceSupportingIndexesExist()
    {
        var beforeIds = await IdsAsync("SELECT Id FROM dbo.V_UnionOrdered;");

        await ExecuteAsync("""
            CREATE INDEX IX_BranchA_Amt ON dbo.BranchA(Amt DESC, Id DESC);
            CREATE INDEX IX_BranchB_Amt ON dbo.BranchB(Amt DESC, Id DESC);
            UPDATE STATISTICS dbo.BranchA WITH FULLSCAN;
            UPDATE STATISTICS dbo.BranchB WITH FULLSCAN;
            """);

        var afterIds = await IdsAsync("SELECT Id FROM dbo.V_UnionOrdered;");

        Assert.NotEqual(beforeIds, afterIds);
    }

    private async Task<List<int>> IdsAsync(string query)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = new SqlCommand(query, connection) { CommandTimeout = 120 };
        await using var reader = await command.ExecuteReaderAsync();

        var ids = new List<int>();
        while (await reader.ReadAsync())
        {
            ids.Add(reader.GetInt32(0));
        }

        return ids;
    }
}

[Trait("Category", "Oracle")]
[Trait("Rule", "silentscan/view/top-percent-order-by-no-op")]
[Trait("Rule", "silentscan/view/order-by-not-guaranteed")]
public sealed class SecondSweepGLiveOracleTests
{
    [Fact]
    public async Task LiveDeployment_BareTopNoOrderBy_Fires()
    {
        var report = await EngineAuthoritativeScan.ScanAsync(
            """
            CREATE TABLE dbo.BareTopTarget (Id INT NOT NULL PRIMARY KEY, Name VARCHAR(50) NOT NULL);
            GO
            CREATE PROCEDURE dbo.usp_BareTopTarget_Find AS
            BEGIN
                SELECT TOP (5) Id, Name FROM dbo.BareTopTarget;
            END
            """,
            minimumConfidence: FindingConfidence.Low);

        var finding = Assert.Single(report.Find<BareTopNoOrderByFinding>("BareTopNoOrderByScanner"));
        Assert.Equal(FindingConfidence.Medium, finding.Confidence);
    }

    [Fact]
    public async Task LiveDeployment_TopWithOrderBy_NeverFires()
    {
        var report = await EngineAuthoritativeScan.ScanAsync(
            """
            CREATE TABLE dbo.BareTopClean (Id INT NOT NULL PRIMARY KEY, Name VARCHAR(50) NOT NULL);
            GO
            CREATE PROCEDURE dbo.usp_BareTopClean_Find AS
            BEGIN
                SELECT TOP (5) Id, Name FROM dbo.BareTopClean ORDER BY Id;
            END
            """,
            minimumConfidence: FindingConfidence.Low);

        Assert.Empty(report.Find<BareTopNoOrderByFinding>("BareTopNoOrderByScanner"));
    }

    [Fact]
    public async Task LiveDeployment_BareTopHundredPointZeroPercent_NeverFires()
    {
        var report = await EngineAuthoritativeScan.ScanAsync(
            """
            CREATE TABLE dbo.BareTopDecimalHundred (Id INT NOT NULL PRIMARY KEY, Name VARCHAR(50) NOT NULL);
            GO
            CREATE PROCEDURE dbo.usp_BareTopDecimalHundred_Find AS
            BEGIN
                SELECT TOP (100.0) PERCENT Id, Name FROM dbo.BareTopDecimalHundred;
            END
            """,
            minimumConfidence: FindingConfidence.Low);

        Assert.Empty(report.Find<BareTopNoOrderByFinding>("BareTopNoOrderByScanner"));
    }

    [Fact]
    public async Task LiveDeployment_ViewTopHundredPointZeroPercentOrderBy_FiresAsNeverLimits()
    {
        var report = await EngineAuthoritativeScan.ScanAsync(
            """
            CREATE TABLE dbo.ViewOrderingDecimalHundred (Id INT NOT NULL PRIMARY KEY, Amt INT NOT NULL);
            GO
            CREATE VIEW dbo.v_ViewOrderingDecimalHundred AS
            SELECT TOP (100.0) PERCENT Id, Amt FROM dbo.ViewOrderingDecimalHundred ORDER BY Amt DESC;
            """,
            minimumConfidence: FindingConfidence.Low);

        var finding = Assert.Single(report.Find<ViewOrderingFinding>("ViewOrderingScanner"));
        Assert.Equal(ViewOrderingFindingKind.TopPercentOrderByNeverLimits, finding.Kind);
        Assert.Equal(FindingConfidence.High, finding.Confidence);
    }

    [Fact]
    public async Task LiveDeployment_ViewUnionTopLevelOrderByOffsetFetch_Fires()
    {
        var report = await EngineAuthoritativeScan.ScanAsync(
            """
            CREATE TABLE dbo.ViewOrderingUnionA (Id INT NOT NULL PRIMARY KEY, Amt INT NOT NULL);
            GO
            CREATE TABLE dbo.ViewOrderingUnionB (Id INT NOT NULL PRIMARY KEY, Amt INT NOT NULL);
            GO
            CREATE VIEW dbo.v_ViewOrderingUnionTopLevel AS
            SELECT Id, Amt FROM dbo.ViewOrderingUnionA
            UNION ALL
            SELECT Id, Amt FROM dbo.ViewOrderingUnionB
            ORDER BY Amt DESC
            OFFSET 0 ROWS FETCH NEXT 10 ROWS ONLY;
            """,
            minimumConfidence: FindingConfidence.Low);

        var finding = Assert.Single(report.Find<ViewOrderingFinding>("ViewOrderingScanner"));
        Assert.Equal(ViewOrderingFindingKind.OrderByNotGuaranteedToConsumer, finding.Kind);
        Assert.Equal(FindingConfidence.Low, finding.Confidence);
    }

    [Fact]
    public async Task LiveDeployment_ViewUnionOrderByNestedInsideOneBranch_DeclinesRatherThanGuessing()
    {
        var report = await EngineAuthoritativeScan.ScanAsync(
            """
            CREATE TABLE dbo.ViewOrderingUnionNestedA (Id INT NOT NULL PRIMARY KEY, Amt INT NOT NULL);
            GO
            CREATE TABLE dbo.ViewOrderingUnionNestedB (Id INT NOT NULL PRIMARY KEY, Amt INT NOT NULL);
            GO
            CREATE VIEW dbo.v_ViewOrderingUnionNested AS
            (SELECT TOP (100) PERCENT Id, Amt FROM dbo.ViewOrderingUnionNestedA ORDER BY Amt)
            UNION ALL
            SELECT Id, Amt FROM dbo.ViewOrderingUnionNestedB;
            """,
            minimumConfidence: FindingConfidence.Low);

        Assert.Empty(report.Find<ViewOrderingFinding>("ViewOrderingScanner"));
    }

    [Fact]
    public async Task LiveDeployment_ViewUnionLastBranchOwnTopWithTrailingOrderBy_DeclinesRatherThanGuessing()
    {
        var report = await EngineAuthoritativeScan.ScanAsync(
            """
            CREATE TABLE dbo.ViewOrderingUnionBranchTopA (Id INT NOT NULL PRIMARY KEY, Amt INT NOT NULL);
            GO
            CREATE TABLE dbo.ViewOrderingUnionBranchTopB (Id INT NOT NULL PRIMARY KEY, Amt INT NOT NULL);
            GO
            CREATE VIEW dbo.v_ViewOrderingUnionBranchTop AS
            SELECT Id, Amt FROM dbo.ViewOrderingUnionBranchTopA
            UNION ALL
            SELECT TOP (1) Id, Amt FROM dbo.ViewOrderingUnionBranchTopB
            ORDER BY Amt DESC;
            """,
            minimumConfidence: FindingConfidence.Low);

        Assert.Empty(report.Find<ViewOrderingFinding>("ViewOrderingScanner"));
    }

    [Fact]
    public async Task LiveDeployment_StringConcatNull_Fires()
    {
        var report = await EngineAuthoritativeScan.ScanAsync(
            """
            CREATE TABLE dbo.ConcatTarget (Id INT NOT NULL PRIMARY KEY, FirstName VARCHAR(50) NOT NULL, MiddleName VARCHAR(50) NULL);
            GO
            CREATE PROCEDURE dbo.usp_ConcatTarget_Find AS
            BEGIN
                SELECT FirstName + ' ' + MiddleName FROM dbo.ConcatTarget;
            END
            """);

        var finding = Assert.Single(report.Find<StringConcatNullFinding>("StringConcatNullScanner"));
        Assert.Equal("dbo.ConcatTarget", finding.TableQualifiedName);
        Assert.Equal("MiddleName", finding.ColumnName);
    }

    [Fact]
    public async Task LiveDeployment_StringConcatGuardedByIsNull_NeverFires()
    {
        var report = await EngineAuthoritativeScan.ScanAsync(
            """
            CREATE TABLE dbo.ConcatClean (Id INT NOT NULL PRIMARY KEY, FirstName VARCHAR(50) NOT NULL, MiddleName VARCHAR(50) NULL);
            GO
            CREATE PROCEDURE dbo.usp_ConcatClean_Find AS
            BEGIN
                SELECT FirstName + ' ' + ISNULL(MiddleName, '') FROM dbo.ConcatClean;
            END
            """);

        Assert.Empty(report.Find<StringConcatNullFinding>("StringConcatNullScanner"));
    }
}
