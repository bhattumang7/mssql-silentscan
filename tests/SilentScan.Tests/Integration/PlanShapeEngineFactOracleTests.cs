using SilentScan.Tests.Support;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class PlanShapeEngineFactOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(PlanShapeEngineFactOracleTests);

    protected override string Ddl => """
        CREATE TABLE dbo.Src (Id INT PRIMARY KEY, K INT NOT NULL);
        INSERT INTO dbo.Src SELECT TOP (5000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)), ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) FROM sys.all_objects a, sys.all_objects b;
        GO
        CREATE TABLE dbo.RlNoIdx (Id INT PRIMARY KEY, TenantId INT NOT NULL, Pad CHAR(100) DEFAULT 'x');
        CREATE TABLE dbo.RlIdx (Id INT PRIMARY KEY, TenantId INT NOT NULL, Pad CHAR(100) DEFAULT 'x');
        INSERT INTO dbo.RlNoIdx (Id, TenantId) SELECT TOP (20000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)), ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) % 100 FROM sys.all_objects a, sys.all_objects b;
        INSERT INTO dbo.RlIdx (Id, TenantId) SELECT Id, TenantId FROM dbo.RlNoIdx;
        CREATE INDEX IX_RlIdx_Tenant ON dbo.RlIdx(TenantId);
        GO
        CREATE SCHEMA sec;
        GO
        CREATE FUNCTION sec.fn(@TenantId INT) RETURNS TABLE WITH SCHEMABINDING AS RETURN SELECT 1 AS ok WHERE @TenantId = CAST(SESSION_CONTEXT(N'tid') AS INT);
        GO
        CREATE SECURITY POLICY sec.pol
            ADD FILTER PREDICATE sec.fn(TenantId) ON dbo.RlNoIdx,
            ADD FILTER PREDICATE sec.fn(TenantId) ON dbo.RlIdx
            WITH (STATE = ON);
        GO
        CREATE TABLE dbo.Fp (Id INT PRIMARY KEY, Status INT NOT NULL, K INT NOT NULL);
        INSERT INTO dbo.Fp SELECT TOP (5000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)), CASE WHEN ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) % 100 = 0 THEN 1 ELSE 0 END, ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) FROM sys.all_objects a, sys.all_objects b;
        CREATE INDEX IX_Fp ON dbo.Fp(K) WHERE Status = 1;
        GO
        CREATE TABLE dbo.Ansi (Id INT PRIMARY KEY, S VARCHAR(10) NOT NULL, K INT NOT NULL);
        INSERT INTO dbo.Ansi SELECT TOP (5000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)), 'a', ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) FROM sys.all_objects a, sys.all_objects b;
        CREATE INDEX IX_Ansi ON dbo.Ansi(K) WHERE S = 'a';
        GO
        CREATE TABLE dbo.E1 (Id INT PRIMARY KEY);
        CREATE TABLE dbo.E2 (Id INT PRIMARY KEY);
        CREATE TABLE dbo.E3 (Id INT PRIMARY KEY);
        GO
        CREATE VIEW dbo.VE1 AS SELECT a.Id FROM dbo.E1 a JOIN dbo.E2 b ON a.Id = b.Id;
        GO
        CREATE VIEW dbo.VE2 AS SELECT v.Id FROM dbo.VE1 v JOIN dbo.E3 c ON v.Id = c.Id;
        GO
        CREATE TABLE dbo.Big (Id INT PRIMARY KEY, V INT NOT NULL);
        INSERT INTO dbo.Big SELECT TOP (200000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)), 1 FROM sys.all_objects a, sys.all_objects b, sys.all_objects c;
        GO
        CREATE FUNCTION dbo.Su(@x INT) RETURNS INT AS BEGIN RETURN @x + 1; END;
        GO
        CREATE VIEW dbo.VSu AS SELECT Id, dbo.Su(V) AS W FROM dbo.Big;
        GO
        CREATE VIEW dbo.VPlain AS SELECT Id, V + 1 AS W FROM dbo.Big;
        GO
        ALTER DATABASE CURRENT SET COMPATIBILITY_LEVEL = 130;
        GO
        CREATE FUNCTION dbo.MsSmall() RETURNS @t TABLE (Id INT, V INT) AS BEGIN INSERT @t VALUES (1, 1), (2, 2); RETURN; END;
        GO
        CREATE FUNCTION dbo.MsBig() RETURNS @t TABLE (Id INT, V INT) AS BEGIN INSERT @t SELECT TOP (5000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)), 1 FROM sys.all_objects a, sys.all_objects b; RETURN; END;
        GO
        CREATE VIEW dbo.VMsSmall AS SELECT Id, V FROM dbo.MsSmall();
        GO
        CREATE VIEW dbo.VMsBig AS SELECT Id, V FROM dbo.MsBig();
        GO
        CREATE VIEW dbo.VMsNested AS SELECT Id, V FROM dbo.VMsBig;
        GO
        """;

    private static readonly string[] ExpandedTables = ["E1", "E2", "E3"];

    private static double FenceEstimate(System.Xml.Linq.XDocument plan) =>
        ShowPlan.EstimatedRows(ShowPlan.Ops(plan, "Table-valued function").First());

    [Fact]
    [Trait("Rule", "silentscan/catalog/rls-predicate-unindexed-column")]
    public async Task FilterPredicateOnUnindexedColumn_ScansTheTable_IndexedControlSeeks()
    {
        const string setup = "EXEC sp_set_session_context 'tid', 7;";

        var scan = await PlanInSessionAsync(setup, "SELECT Id FROM dbo.RlNoIdx;");
        var seek = await PlanInSessionAsync(setup, "SELECT Id FROM dbo.RlIdx;");

        Assert.DoesNotContain("Index Seek", ShowPlan.PhysicalOpsOnTable(scan, "RlNoIdx"));
        Assert.Contains("Clustered Index Scan", ShowPlan.PhysicalOpsOnTable(scan, "RlNoIdx"));
        Assert.Contains("Index Seek", ShowPlan.PhysicalOpsOnTable(seek, "RlIdx"));
        Assert.Contains("IX_RlIdx_Tenant", ShowPlan.IndexNames(seek));
    }

    [Fact]
    [Trait("Rule", "silentscan/tvf-fence/nested-under-view-or-tvf")]
    public async Task MultiStatementTvfBehindViews_KeepsTheSameFixedEstimate()
    {
        await ExecuteAsync("ALTER DATABASE CURRENT SET COMPATIBILITY_LEVEL = 130;");
        var small = await PlanInSessionAsync(string.Empty, "SELECT Id FROM dbo.VMsSmall;");
        var big = await PlanInSessionAsync(string.Empty, "SELECT Id FROM dbo.VMsBig;");
        var nested = await PlanInSessionAsync(string.Empty, "SELECT Id FROM dbo.VMsNested;");

        Assert.Equal(100, FenceEstimate(small));
        Assert.Equal(100, FenceEstimate(big));
        Assert.Equal(100, FenceEstimate(nested));
    }

    [Fact]
    [Trait("Rule", "silentscan/scalar-udf/nested-under-view-or-tvf")]
    public async Task ScalarUdfInsideView_ForcesSerialPlanAtCompat140_PlainViewControlCanGoParallel()
    {
        await ExecuteAsync("ALTER DATABASE CURRENT SET COMPATIBILITY_LEVEL = 140;");

        var udf = await PlanInSessionAsync(string.Empty, "SELECT SUM(W) FROM dbo.VSu OPTION (RECOMPILE);");
        var plain = await PlanInSessionAsync(string.Empty, "SELECT SUM(W) FROM dbo.VPlain OPTION (RECOMPILE);");

        Assert.Equal("TSQLUserDefinedFunctionsNotParallelizable", ShowPlan.NonParallelPlanReason(udf));
        Assert.NotEqual("TSQLUserDefinedFunctionsNotParallelizable", ShowPlan.NonParallelPlanReason(plain));
    }

    [Fact]
    [Trait("Rule", "silentscan/lineage/post-expansion-join-width")]
    public async Task ViewReference_ExpandsToEveryUnderlyingTable_BaseTableControlTouchesOne()
    {
        var expanded = await PlanInSessionAsync(string.Empty, "SELECT Id FROM dbo.VE2;");
        var direct = await PlanInSessionAsync(string.Empty, "SELECT Id FROM dbo.E1;");

        Assert.True(ExpandedTables.All(ShowPlan.TableNames(expanded).Contains));
        Assert.Equal(["E1"], ShowPlan.TableNames(direct).ToArray());
    }

    [Fact]
    [Trait("Rule", "silentscan/temp-table/unindexed-where-filter")]
    public async Task UnindexedTempTableFilter_Scans_IndexedTempTableSeeks()
    {
        const string setup = "SELECT * INTO #t1 FROM dbo.Src; SELECT * INTO #t2 FROM dbo.Src; CREATE INDEX IX_t2 ON #t2(K);";

        var unindexed = await PlanInSessionAsync(setup, "SELECT Id FROM #t1 WHERE K = 42;");
        var indexed = await PlanInSessionAsync(setup, "SELECT Id FROM #t2 WHERE K = 42;");

        Assert.Empty(ShowPlan.IndexNames(unindexed));
        Assert.DoesNotContain("Index Seek", ShowPlan.PhysicalOps(unindexed));
        Assert.Contains("IX_t2", ShowPlan.IndexNames(indexed));
        Assert.Contains("Index Seek", ShowPlan.PhysicalOps(indexed));
    }

    [Fact]
    [Trait("Rule", "silentscan/predicates/filtered-index-parameter-mismatch")]
    public async Task FilteredIndexIsUsedForLiteralButNotForVariable()
    {
        var literal = await PlanInSessionAsync(string.Empty, "SELECT K FROM dbo.Fp WHERE Status = 1;");
        var variable = await PlanInSessionAsync(string.Empty, "DECLARE @s INT = 1; SELECT K FROM dbo.Fp WHERE Status = @s;");

        Assert.Contains("IX_Fp", ShowPlan.IndexNames(literal));
        Assert.DoesNotContain("IX_Fp", ShowPlan.IndexNames(variable));
    }

    [Fact]
    [Trait("Rule", "silentscan/set-option/ansi-padding-off")]
    public async Task FilteredIndexIsIgnoredUnderAnsiPaddingOff_UsedUnderOn()
    {
        const string probe = "SELECT K FROM dbo.Ansi WHERE S = 'a' AND K = 5;";

        var on = await PlanInSessionAsync("SET ANSI_PADDING ON;", probe);
        var off = await PlanInSessionAsync("SET ANSI_PADDING OFF;", probe);

        Assert.Contains("IX_Ansi", ShowPlan.IndexNames(on));
        Assert.DoesNotContain("IX_Ansi", ShowPlan.IndexNames(off));
    }
}
