using SilentScan.Tests.Support;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
[Collection("ServerLevelConfiguration")]
public sealed class CatalogConstraintAndViewEngineFactOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(CatalogConstraintAndViewEngineFactOracleTests);

    protected override string Ddl => """
        CREATE TABLE dbo.ChkNull (Id INT NOT NULL, Qty INT NULL, CONSTRAINT CK_ChkNull CHECK (Qty > 0));
        GO
        CREATE TABLE dbo.ChkNullSafe (Id INT NOT NULL, Qty INT NULL, CONSTRAINT CK_ChkNullSafe CHECK (Qty IS NOT NULL AND Qty > 0));
        GO
        CREATE TABLE dbo.ChkIdent (Id INT IDENTITY(1,1) NOT NULL, V INT NULL, CONSTRAINT CK_ChkIdent CHECK (Id > 2));
        GO
        CREATE TABLE dbo.DfNullable (Id INT NOT NULL, Status INT NULL CONSTRAINT DF_DfNullable DEFAULT 5);
        GO
        CREATE TABLE dbo.DfNotNull (Id INT NOT NULL, Status INT NOT NULL CONSTRAINT DF_DfNotNull DEFAULT 5);
        GO
        CREATE TABLE dbo.DfNullDefault (Id INT NOT NULL, Status INT NULL CONSTRAINT DF_DfNullDefault DEFAULT NULL);
        GO
        CREATE TABLE dbo.ParCascade (Id INT PRIMARY KEY);
        CREATE TABLE dbo.ChdCascade (Id INT PRIMARY KEY, PId INT NOT NULL CONSTRAINT FK_ChdCascade FOREIGN KEY REFERENCES dbo.ParCascade(Id) ON DELETE CASCADE);
        CREATE TABLE dbo.ParPlain (Id INT PRIMARY KEY);
        CREATE TABLE dbo.ChdPlain (Id INT PRIMARY KEY, PId INT NOT NULL CONSTRAINT FK_ChdPlain FOREIGN KEY REFERENCES dbo.ParPlain(Id));
        INSERT INTO dbo.ParCascade VALUES (1), (2);
        INSERT INTO dbo.ChdCascade VALUES (10, 1), (11, 1), (12, 2);
        INSERT INTO dbo.ParPlain VALUES (1), (2);
        INSERT INTO dbo.ChdPlain VALUES (10, 1), (11, 1), (12, 2);
        CREATE TABLE dbo.ParCascadeOff (Id INT PRIMARY KEY);
        CREATE TABLE dbo.ChdCascadeOff (Id INT PRIMARY KEY, PId INT NOT NULL CONSTRAINT FK_ChdCascadeOff FOREIGN KEY REFERENCES dbo.ParCascadeOff(Id) ON DELETE CASCADE);
        INSERT INTO dbo.ParCascadeOff VALUES (1), (2);
        INSERT INTO dbo.ChdCascadeOff VALUES (10, 1), (11, 1), (12, 2);
        ALTER TABLE dbo.ChdCascadeOff NOCHECK CONSTRAINT FK_ChdCascadeOff;
        CREATE TABLE dbo.ParCascadeOn (Id INT PRIMARY KEY);
        CREATE TABLE dbo.ChdCascadeOn (Id INT PRIMARY KEY, PId INT NOT NULL CONSTRAINT FK_ChdCascadeOn FOREIGN KEY REFERENCES dbo.ParCascadeOn(Id) ON DELETE CASCADE);
        INSERT INTO dbo.ParCascadeOn VALUES (1), (2);
        INSERT INTO dbo.ChdCascadeOn VALUES (10, 1), (11, 1), (12, 2);
        GO
        CREATE TABLE dbo.Lob (Id INT PRIMARY KEY, Body TEXT NULL, BodyMax VARCHAR(MAX) NULL);
        GO
        CREATE FUNCTION dbo.TvfDbCollation() RETURNS @t TABLE (N VARCHAR(10)) AS BEGIN RETURN; END;
        GO
        CREATE FUNCTION dbo.TvfExplicitCollation() RETURNS @t TABLE (N VARCHAR(10) COLLATE Latin1_General_CS_AS) AS BEGIN RETURN; END;
        GO
        CREATE PROCEDURE dbo.ProcRecompile WITH RECOMPILE AS SELECT 1 AS X;
        GO
        CREATE PROCEDURE dbo.ProcCached AS SELECT 1 AS X;
        GO
        CREATE TABLE dbo.Base (A INT, B INT, C INT);
        INSERT INTO dbo.Base VALUES (1, 2, 3);
        GO
        CREATE VIEW dbo.VStar AS SELECT * FROM dbo.Base;
        GO
        CREATE VIEW dbo.VStarOuter AS SELECT * FROM dbo.VStar;
        GO
        CREATE VIEW dbo.VNamed AS SELECT A, B, C FROM dbo.Base;
        GO
        """;

    [Fact]
    [Trait("Rule", FindingRuleIds.CheckConstraintNullNotHandledRuleId)]
    public async Task CheckConstraintWithoutNullTest_LetsNullThrough_WhileNullSafeControlRejectsIt()
    {
        Assert.Null(await SqlErrorNumberAsync("INSERT INTO dbo.ChkNull VALUES (1, NULL);"));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.ChkNull WHERE Qty IS NULL;"));
        Assert.Equal(547, await SqlErrorNumberAsync("INSERT INTO dbo.ChkNull VALUES (2, -1);"));
        Assert.Equal(547, await SqlErrorNumberAsync("INSERT INTO dbo.ChkNullSafe VALUES (1, NULL);"));
    }

    [Fact]
    [Trait("Rule", FindingRuleIds.CheckConstraintOnIdentityColumnRuleId)]
    public async Task CheckOnIdentity_FailedInsertsStillAdvanceTheCounter()
    {
        Assert.Equal(547, await SqlErrorNumberAsync("INSERT INTO dbo.ChkIdent (V) VALUES (1);"));
        Assert.Equal(547, await SqlErrorNumberAsync("INSERT INTO dbo.ChkIdent (V) VALUES (1);"));
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.ChkIdent;"));

        var id = await ScalarAsync<int>("INSERT INTO dbo.ChkIdent (V) OUTPUT inserted.Id VALUES (1);");

        Assert.Equal(3, id);
    }

    [Fact]
    [Trait("Rule", FindingRuleIds.ModuleCompileFlagRecompilesEveryCallRuleId)]
    public async Task WithRecompileProcedure_IsFlaggedAndLeavesNoCachedPlan_WhileControlIsCached()
    {
        await ExecuteAsync("EXEC dbo.ProcRecompile; EXEC dbo.ProcRecompile; EXEC dbo.ProcCached; EXEC dbo.ProcCached;");

        var flags = await RowsAsync("SELECT OBJECT_NAME(object_id), CAST(is_recompiled AS INT) FROM sys.sql_modules WHERE object_id IN (OBJECT_ID('dbo.ProcRecompile'), OBJECT_ID('dbo.ProcCached')) ORDER BY 1;");
        var cached = await RowsAsync("SELECT OBJECT_NAME(st.objectid, st.dbid) FROM sys.dm_exec_cached_plans cp CROSS APPLY sys.dm_exec_sql_text(cp.plan_handle) st WHERE cp.objtype = 'Proc' AND st.dbid = DB_ID() AND st.objectid IN (OBJECT_ID('dbo.ProcRecompile'), OBJECT_ID('dbo.ProcCached'));");

        Assert.Equal(1, flags.Single(r => (string)r[0]! == "ProcRecompile")[1]);
        Assert.Equal(0, flags.Single(r => (string)r[0]! == "ProcCached")[1]);
        Assert.DoesNotContain(cached, r => (string)r[0]! == "ProcRecompile");
        Assert.Contains(cached, r => (string)r[0]! == "ProcCached");
    }

    [Fact]
    [Trait("Rule", FindingRuleIds.ModuleCompileFlagTableValuedFunctionReturnUsesDatabaseCollationRuleId)]
    public async Task TvfReturnColumnWithoutCollate_BakesInDatabaseCollation_ExplicitCollateControlDoesNot()
    {
        var rows = await RowsAsync("SELECT OBJECT_NAME(object_id), CAST(uses_database_collation AS INT) FROM sys.sql_modules WHERE object_id IN (OBJECT_ID('dbo.TvfDbCollation'), OBJECT_ID('dbo.TvfExplicitCollation'));");

        Assert.Equal(1, rows.Single(r => (string)r[0]! == "TvfDbCollation")[1]);
        Assert.Equal(0, rows.Single(r => (string)r[0]! == "TvfExplicitCollation")[1]);
    }

    [Fact]
    [Trait("Rule", FindingRuleIds.StaleSelectStarViewRuleId)]
    [Trait("Rule", FindingRuleIds.SelectStarViewRuleId)]
    public async Task SelectStarView_StaysFrozenAfterAlterTableAdd_UntilRefreshed()
    {
        await using var connection = await OpenConnectionAsync();
        await ExecuteAsync(connection, "ALTER TABLE dbo.Base ADD D INT NULL;");

        var starColumns = await ScalarAsync<int>(connection, "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.VStar');");
        var described = await RowsAsync(connection, "SELECT name FROM sys.dm_exec_describe_first_result_set(N'SELECT * FROM dbo.VStar', NULL, 0);");
        var bareStarWidth = (await RowsAsync(connection, "SELECT * FROM dbo.Base;"))[0].Length;
        var viewWidth = (await RowsAsync(connection, "SELECT * FROM dbo.VStar;"))[0].Length;
        var namedWidth = (await RowsAsync(connection, "SELECT * FROM dbo.VNamed;"))[0].Length;

        Assert.Equal(3, starColumns);
        Assert.Equal(3, described.Count);
        Assert.Equal(4, bareStarWidth);
        Assert.Equal(3, viewWidth);
        Assert.Equal(3, namedWidth);

        await ExecuteAsync(connection, "EXEC sp_refreshview 'dbo.VStar';");
        Assert.Equal(4, await ScalarAsync<int>(connection, "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.VStar');"));
    }
}
