using SilentScan.Tests.Support;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
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
        CREATE TABLE dbo.ParCascade (Id INT PRIMARY KEY);
        CREATE TABLE dbo.ChdCascade (Id INT PRIMARY KEY, PId INT NOT NULL CONSTRAINT FK_ChdCascade FOREIGN KEY REFERENCES dbo.ParCascade(Id) ON DELETE CASCADE);
        CREATE TABLE dbo.ParPlain (Id INT PRIMARY KEY);
        CREATE TABLE dbo.ChdPlain (Id INT PRIMARY KEY, PId INT NOT NULL CONSTRAINT FK_ChdPlain FOREIGN KEY REFERENCES dbo.ParPlain(Id));
        INSERT INTO dbo.ParCascade VALUES (1), (2);
        INSERT INTO dbo.ChdCascade VALUES (10, 1), (11, 1), (12, 2);
        INSERT INTO dbo.ParPlain VALUES (1), (2);
        INSERT INTO dbo.ChdPlain VALUES (10, 1), (11, 1), (12, 2);
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
    [Trait("Rule", "silentscan/catalog/check-constraint-null-not-handled")]
    public async Task CheckConstraintWithoutNullTest_LetsNullThrough_WhileNullSafeControlRejectsIt()
    {
        Assert.Null(await SqlErrorNumberAsync("INSERT INTO dbo.ChkNull VALUES (1, NULL);"));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.ChkNull WHERE Qty IS NULL;"));
        Assert.Equal(547, await SqlErrorNumberAsync("INSERT INTO dbo.ChkNull VALUES (2, -1);"));
        Assert.Equal(547, await SqlErrorNumberAsync("INSERT INTO dbo.ChkNullSafe VALUES (1, NULL);"));
    }

    [Fact]
    [Trait("Rule", "silentscan/catalog/check-constraint-on-identity-column")]
    public async Task CheckOnIdentity_FailedInsertsStillAdvanceTheCounter()
    {
        Assert.Equal(547, await SqlErrorNumberAsync("INSERT INTO dbo.ChkIdent (V) VALUES (1);"));
        Assert.Equal(547, await SqlErrorNumberAsync("INSERT INTO dbo.ChkIdent (V) VALUES (1);"));
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.ChkIdent;"));

        var id = await ScalarAsync<int>("INSERT INTO dbo.ChkIdent (V) OUTPUT inserted.Id VALUES (1);");

        Assert.Equal(3, id);
    }

    [Fact]
    [Trait("Rule", "silentscan/catalog/default-constraint-on-nullable-column")]
    public async Task ExplicitNullBypassesDefaultOnNullableColumn_ButNotNullControlRejectsIt()
    {
        await ExecuteAsync("INSERT INTO dbo.DfNullable (Id) VALUES (1); INSERT INTO dbo.DfNullable (Id, Status) VALUES (2, NULL);");

        var rows = await RowsAsync("SELECT Id, Status FROM dbo.DfNullable ORDER BY Id;");

        Assert.Equal(5, rows[0][1]);
        Assert.Null(rows[1][1]);
        Assert.Equal(515, await SqlErrorNumberAsync("INSERT INTO dbo.DfNotNull (Id, Status) VALUES (2, NULL);"));
    }

    [Fact]
    [Trait("Rule", "silentscan/catalog/cascading-foreign-key")]
    public async Task DeletingParentCascadesToChildRows_ButPlainForeignKeyControlBlocksTheDelete()
    {
        await ExecuteAsync("DELETE FROM dbo.ParCascade WHERE Id = 1;");

        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.ChdCascade;"));
        Assert.Equal(547, await SqlErrorNumberAsync("DELETE FROM dbo.ParPlain WHERE Id = 1;"));
        Assert.Equal(3, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.ChdPlain;"));
    }

    [Fact]
    [Trait("Rule", "silentscan/index-design/deprecated-lob-column-type")]
    public async Task TextColumnCannotBeIndexedOrIncluded_ButMaxColumnCanBeIncluded()
    {
        Assert.Equal(1919, await SqlErrorNumberAsync("CREATE INDEX IX_Lob_Key ON dbo.Lob(Body);"));
        Assert.Equal(1999, await SqlErrorNumberAsync("CREATE INDEX IX_Lob_Incl ON dbo.Lob(Id) INCLUDE (Body);"));
        Assert.Null(await SqlErrorNumberAsync("CREATE INDEX IX_Lob_Max ON dbo.Lob(Id) INCLUDE (BodyMax);"));
    }

    [Fact]
    [Trait("Rule", "silentscan/index-design/deprecated-lob-column-type")]
    public async Task DeclaringATextColumn_IncrementsTheDeprecatedFeatureCounter()
    {
        const string counter = "SELECT ISNULL(SUM(cntr_value), 0) FROM sys.dm_os_performance_counters WHERE object_name LIKE '%Deprecated Features%' AND instance_name LIKE 'Data types: text%';";
        var before = await ScalarAsync<long>(counter);
        await ExecuteAsync("CREATE TABLE dbo.MaxProbe (Id INT, B VARCHAR(MAX));");
        var afterMax = await ScalarAsync<long>(counter);
        await ExecuteAsync("CREATE TABLE dbo.TextProbe (Id INT, B TEXT);");
        var afterText = await ScalarAsync<long>(counter);

        Assert.Equal(before, afterMax);
        Assert.True(afterText > before);
    }

    [Fact]
    [Trait("Rule", "silentscan/catalog/with-recompile")]
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
    [Trait("Rule", "silentscan/catalog/tvf-return-database-collation")]
    public async Task TvfReturnColumnWithoutCollate_BakesInDatabaseCollation_ExplicitCollateControlDoesNot()
    {
        var rows = await RowsAsync("SELECT OBJECT_NAME(object_id), CAST(uses_database_collation AS INT) FROM sys.sql_modules WHERE object_id IN (OBJECT_ID('dbo.TvfDbCollation'), OBJECT_ID('dbo.TvfExplicitCollation'));");

        Assert.Equal(1, rows.Single(r => (string)r[0]! == "TvfDbCollation")[1]);
        Assert.Equal(0, rows.Single(r => (string)r[0]! == "TvfExplicitCollation")[1]);
    }

    [Fact]
    [Trait("Rule", "silentscan/catalog/stale-select-star-view")]
    [Trait("Rule", "silentscan/lineage/select-star-view")]
    [Trait("Rule", "silentscan/statement-shape/bare-select-star")]
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

    [Fact]
    [Trait("Rule", "silentscan/lineage/nested-view-depth")]
    public async Task NestedSelectStarViews_RefreshingOnlyTheOuterViewDoesNotSurfaceTheNewColumn()
    {
        await using var connection = await OpenConnectionAsync();
        await ExecuteAsync(connection, "ALTER TABLE dbo.Base ADD E INT NULL;");

        await ExecuteAsync(connection, "EXEC sp_refreshview 'dbo.VStarOuter';");
        var afterOuterOnly = await ScalarAsync<int>(connection, "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.VStarOuter');");

        await ExecuteAsync(connection, "EXEC sp_refreshview 'dbo.VStar'; EXEC sp_refreshview 'dbo.VStarOuter';");
        var afterBoth = await ScalarAsync<int>(connection, "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.VStarOuter');");

        Assert.Equal(3, afterOuterOnly);
        Assert.Equal(4, afterBoth);
    }
}
