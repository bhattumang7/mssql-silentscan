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
        CREATE TABLE dbo.ChkIsnullRejects (Id INT NOT NULL, Qty INT NULL, CONSTRAINT CK_ChkIsnullRejects CHECK (ISNULL(Qty, 0) > 0));
        GO
        CREATE TABLE dbo.ChkCoalesceRejects (Id INT NOT NULL, Qty INT NULL, CONSTRAINT CK_ChkCoalesceRejects CHECK (COALESCE(Qty, 0) > 0));
        GO
        CREATE FUNCTION dbo.ChkNullPositive(@v INT) RETURNS BIT AS BEGIN RETURN CASE WHEN @v > 0 THEN 1 ELSE 0 END; END;
        GO
        CREATE TABLE dbo.ChkNullUdf (Id INT NOT NULL, Qty INT NULL, CONSTRAINT CK_ChkNullUdf CHECK (dbo.ChkNullPositive(Qty) = 1));
        CREATE TABLE dbo.ChkNullLen (Id INT NOT NULL, Qty VARCHAR(10) NULL, CONSTRAINT CK_ChkNullLen CHECK (LEN(Qty) > 0));
        GO
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
    public async Task CheckConstraintThatSubstitutesNullInsidePredicate_RejectsNull_UnlikeAPlainComparison()
    {
        Assert.Equal(547, await SqlErrorNumberAsync("INSERT INTO dbo.ChkIsnullRejects VALUES (1, NULL);"));
        Assert.Equal(547, await SqlErrorNumberAsync("INSERT INTO dbo.ChkCoalesceRejects VALUES (1, NULL);"));
        Assert.Null(await SqlErrorNumberAsync("INSERT INTO dbo.ChkNull VALUES (3, NULL);"));
    }

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
    [Trait("Rule", FindingRuleIds.CheckConstraintNullNotHandledRuleId)]
    public async Task NullPassedToUserFunction_IsDecidedWithoutUnknown_WhileNullPropagatingExpressionLetsNullThrough()
    {
        Assert.Equal(547, await SqlErrorNumberAsync("INSERT INTO dbo.ChkNullUdf VALUES (1, NULL);"));
        Assert.Null(await SqlErrorNumberAsync("INSERT INTO dbo.ChkNullLen VALUES (1, NULL);"));
        Assert.Equal(547, await SqlErrorNumberAsync("INSERT INTO dbo.ChkNullLen VALUES (2, '');"));
    }

    [Fact]
    [Trait("Rule", FindingRuleIds.StaleSelectStarViewRuleId)]
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
