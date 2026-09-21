using SilentScan.Tests.Support;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class ControlFlowAndStatementSemanticsEngineFactOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(ControlFlowAndStatementSemanticsEngineFactOracleTests);

    protected override string Ddl => """
        CREATE TABLE dbo.N (Id INT, V INT NULL);
        INSERT INTO dbo.N VALUES (1, NULL), (2, 5);
        GO
        CREATE TABLE dbo.A1 (Id INT IDENTITY(1,1), V INT);
        CREATE TABLE dbo.Aud (Id INT IDENTITY(1000,1), Note INT);
        GO
        CREATE TRIGGER dbo.trg_A1 ON dbo.A1 AFTER INSERT AS INSERT INTO dbo.Aud (Note) VALUES (1);
        GO
        CREATE PROCEDURE dbo.GlobalCursor AS BEGIN DECLARE c CURSOR FOR SELECT 1; OPEN c; END;
        GO
        CREATE PROCEDURE dbo.LocalCursor AS BEGIN DECLARE c CURSOR LOCAL FOR SELECT 1; OPEN c; END;
        GO
        CREATE TABLE dbo.X (a INT);
        CREATE TABLE dbo.Y (b INT);
        INSERT INTO dbo.X VALUES (1), (2), (3);
        INSERT INTO dbo.Y VALUES (1), (2), (3), (4);
        GO
        CREATE TABLE dbo.Tg (Id INT PRIMARY KEY);
        CREATE TABLE dbo.TgCond (Id INT PRIMARY KEY);
        INSERT INTO dbo.Tg VALUES (1), (2), (3);
        INSERT INTO dbo.TgCond VALUES (1), (2), (3);
        GO
        CREATE TABLE dbo.W (Id INT, V INT);
        INSERT INTO dbo.W VALUES (1, 0), (2, 0), (3, 0);
        GO
        CREATE TABLE dbo.H (K INT, V INT);
        INSERT INTO dbo.H VALUES (1, 1), (1, 2), (2, 3);
        GO
        CREATE TABLE dbo.O (X INT, Y INT);
        INSERT INTO dbo.O VALUES (1, 9), (2, 8);
        GO
        CREATE TABLE dbo.Fl (F FLOAT NOT NULL, Dc DECIMAL(10,1) NOT NULL);
        CREATE INDEX IX_Fl_F ON dbo.Fl(F);
        CREATE INDEX IX_Fl_Dc ON dbo.Fl(Dc);
        INSERT INTO dbo.Fl SELECT CAST(0.1 AS FLOAT) + CAST(0.2 AS FLOAT), CAST(0.1 AS DECIMAL(10,1)) + CAST(0.2 AS DECIMAL(10,1));
        GO
        CREATE TABLE dbo.Pa (Id INT NOT NULL, Grp INT NOT NULL);
        CREATE TABLE dbo.Pb (PaId INT NOT NULL, Tag INT NOT NULL);
        INSERT INTO dbo.Pa VALUES (1, 1), (2, 1);
        INSERT INTO dbo.Pb VALUES (1, 7), (1, 8), (2, 7);
        CREATE TABLE dbo.Pu (PaId INT NOT NULL, Tag INT NOT NULL);
        CREATE UNIQUE INDEX UX_Pu ON dbo.Pu(PaId);
        INSERT INTO dbo.Pu VALUES (1, 7), (2, 7);
        GO
        """;

    [Fact]
    [Trait("Rule", "silentscan/deprecated-syntax/equals-null-comparison")]
    [Trait("Rule", "silentscan/deprecated-syntax/not-equals-null-comparison")]
    public async Task EqualsAndNotEqualsNull_MatchNoRows_WhileIsNullMatches()
    {
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.N WHERE V = NULL;"));
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.N WHERE V <> NULL;"));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.N WHERE V IS NULL;"));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.N WHERE V IS NOT NULL;"));
    }

    [Fact]
    [Trait("Rule", "silentscan/control-flow/case-expression-missing-else")]
    public async Task SimpleCaseWithNoMatchAndNoElse_ReturnsNull_ElseControlDoesNot()
    {
        Assert.Null(await ScalarAsync<string>("SELECT CASE 5 WHEN 1 THEN 'a' WHEN 2 THEN 'b' END;"));
        Assert.Equal("z", await ScalarAsync<string>("SELECT CASE 5 WHEN 1 THEN 'a' WHEN 2 THEN 'b' ELSE 'z' END;"));
    }

    [Fact]
    [Trait("Rule", "silentscan/control-flow/non-deterministic-case-input")]
    public async Task SimpleCaseOverNewid_IsReEvaluatedPerWhen_DeterministicControlNeverYieldsNull()
    {
        const string rows = "WITH n AS (SELECT TOP (3000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) r FROM sys.all_objects a CROSS JOIN sys.all_objects b) ";
        var nondeterministic = await ScalarAsync<int>(rows + "SELECT SUM(CASE WHEN x IS NULL THEN 1 ELSE 0 END) FROM (SELECT CASE ABS(CHECKSUM(NEWID())) % 3 WHEN 0 THEN 'a' WHEN 1 THEN 'b' WHEN 2 THEN 'c' END AS x FROM n) q;");
        var deterministic = await ScalarAsync<int>(rows + "SELECT SUM(CASE WHEN x IS NULL THEN 1 ELSE 0 END) FROM (SELECT CASE r % 3 WHEN 0 THEN 'a' WHEN 1 THEN 'b' WHEN 2 THEN 'c' END AS x FROM n) q;");

        Assert.True(nondeterministic > 0);
        Assert.Equal(0, deterministic);
    }

    [Fact]
    [Trait("Rule", "silentscan/control-flow/legacy-identity-intrinsic")]
    public async Task AtAtIdentityReturnsTriggerScopeValue_ScopeIdentityReturnsOwnScopeValue()
    {
        var rows = await RowsAsync("INSERT INTO dbo.A1 (V) VALUES (1); SELECT CAST(@@IDENTITY AS INT), CAST(SCOPE_IDENTITY() AS INT);");

        Assert.Equal(1000, rows[0][0]);
        Assert.Equal(1, rows[0][1]);
    }

    [Fact]
    [Trait("Rule", "silentscan/control-flow/empty-catch-block")]
    public async Task EmptyCatch_SwallowsErrorAndBatchContinues_UncaughtControlSurfacesError()
    {
        await using var connection = await OpenConnectionAsync();

        Assert.Null(await SqlErrorNumberAsync(connection, "BEGIN TRY SELECT 1/0; END TRY BEGIN CATCH END CATCH"));
        Assert.Equal(8134, await SqlErrorNumberAsync(connection, "SELECT 1/0;"));
        Assert.Equal(5, await ScalarAsync<int>(connection, "DECLARE @r INT = 0; BEGIN TRY SET @r = 1/0; END TRY BEGIN CATCH END CATCH SELECT @r + 5;"));
    }

    [Fact]
    [Trait("Rule", "silentscan/query/global-cursor-declaration")]
    public async Task GlobalCursor_SurvivesTheProcedure_LocalCursorDoesNot()
    {
        await using var connection = await OpenConnectionAsync();

        await ExecuteAsync(connection, "EXEC dbo.LocalCursor;");
        Assert.Equal(-3, await ScalarAsync<int>(connection, "SELECT CURSOR_STATUS('global', 'c');"));

        await ExecuteAsync(connection, "EXEC dbo.GlobalCursor;");
        Assert.Equal(1, await ScalarAsync<int>(connection, "SELECT CURSOR_STATUS('global', 'c');"));
        await ExecuteAsync(connection, "CLOSE c; DEALLOCATE c;");
    }

    [Fact]
    [Trait("Rule", "silentscan/join/cartesian-comma-join")]
    [Trait("Rule", "silentscan/join/cartesian-cross-join")]
    public async Task CommaAndCrossJoinWithoutPredicate_ProduceFullProduct_PredicateControlDoesNot()
    {
        Assert.Equal(12, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.X, dbo.Y;"));
        Assert.Equal(12, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.X CROSS JOIN dbo.Y;"));
        Assert.Equal(3, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.X x JOIN dbo.Y y ON x.a = y.b;"));
    }

    [Fact]
    [Trait("Rule", "silentscan/join/join-predicate-empty-with-where-clause")]
    public async Task JoinWhoseWhereClauseContradictsItsOn_ReturnsNothing_CompatibleWhereControlReturnsRows()
    {
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.X x JOIN dbo.Y y ON x.a = y.b WHERE x.a = 1 AND y.b = 2;"));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.X x JOIN dbo.Y y ON x.a = y.b WHERE x.a = 1 AND y.b = 1;"));
    }

    [Fact]
    [Trait("Rule", "silentscan/query/merge-unconditional-delete")]
    public async Task MergeNotMatchedBySourceDelete_WithEmptySourceDeletesEverything_ConditionedBranchDoesNot()
    {
        await ExecuteAsync("MERGE dbo.TgCond t USING (SELECT Id FROM dbo.TgCond WHERE 1 = 0) s ON t.Id = s.Id WHEN NOT MATCHED BY SOURCE AND t.Id > 100 THEN DELETE;");
        Assert.Equal(3, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.TgCond;"));

        await ExecuteAsync("MERGE dbo.Tg t USING (SELECT Id FROM dbo.Tg WHERE 1 = 0) s ON t.Id = s.Id WHEN NOT MATCHED BY SOURCE THEN DELETE;");
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Tg;"));
    }

    [Fact]
    [Trait("Rule", "silentscan/query/unbounded-table-write")]
    public async Task UpdateWithoutWhere_TouchesEveryRow_WhereControlTouchesOne()
    {
        await ExecuteAsync("UPDATE dbo.W SET V = 5 WHERE Id = 1;");
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.W WHERE V = 5;"));

        await ExecuteAsync("UPDATE dbo.W SET V = 9;");
        Assert.Equal(3, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.W WHERE V = 9;"));
    }

    [Fact]
    [Trait("Rule", "silentscan/query/non-aggregate-having-predicate")]
    public async Task NonAggregateHaving_EqualsWhereResult_AndPlansAreIdentical()
    {
        const string having = "SELECT K, SUM(V) FROM dbo.H GROUP BY K HAVING K = 1";
        const string where = "SELECT K, SUM(V) FROM dbo.H WHERE K = 1 GROUP BY K";

        var havingRows = await RowsAsync(having);
        var whereRows = await RowsAsync(where);
        var havingOps = ShowPlan.PhysicalOps(await PlanInSessionAsync(string.Empty, having));
        var whereOps = ShowPlan.PhysicalOps(await PlanInSessionAsync(string.Empty, where));

        Assert.Equal(whereRows.Count, havingRows.Count);
        Assert.Equal(whereRows[0][1], havingRows[0][1]);
        Assert.Equal(whereOps, havingOps);
    }

    [Fact]
    [Trait("Rule", "silentscan/statement-shape/insert-without-column-list")]
    public async Task PositionalInsert_LandsInTheWrongColumnAfterTheTableIsRecreatedWithSwappedColumns()
    {
        await ExecuteAsync("CREATE TABLE dbo.S1 (A INT, B INT); INSERT INTO dbo.S1 VALUES (1, 2);");
        var before = await ScalarAsync<int>("SELECT A FROM dbo.S1;");
        await ExecuteAsync("DROP TABLE dbo.S1; CREATE TABLE dbo.S1 (B INT, A INT); INSERT INTO dbo.S1 VALUES (1, 2);");
        var positional = await ScalarAsync<int>("SELECT A FROM dbo.S1;");
        await ExecuteAsync("DELETE FROM dbo.S1; INSERT INTO dbo.S1 (A, B) VALUES (1, 2);");
        var named = await ScalarAsync<int>("SELECT A FROM dbo.S1;");

        Assert.Equal(1, before);
        Assert.Equal(2, positional);
        Assert.Equal(1, named);
    }

    [Fact]
    [Trait("Rule", "silentscan/statement-shape/ordinal-order-by")]
    public async Task OrdinalOrderBy_ChangesFirstRowWhenSelectListIsReordered_NamedControlDoesNot()
    {
        var ordinalXFirst = await RowsAsync("SELECT TOP 1 X, Y FROM dbo.O ORDER BY 2;");
        var ordinalYFirst = await RowsAsync("SELECT TOP 1 Y, X FROM dbo.O ORDER BY 2;");
        var namedXFirst = await RowsAsync("SELECT TOP 1 X, Y FROM dbo.O ORDER BY Y;");
        var namedYFirst = await RowsAsync("SELECT TOP 1 Y, X FROM dbo.O ORDER BY Y;");

        Assert.Equal(2, ordinalXFirst[0][0]);
        Assert.Equal(1, ordinalYFirst[0][1]);
        Assert.Equal(2, namedXFirst[0][0]);
        Assert.Equal(2, namedYFirst[0][1]);
    }

    [Fact]
    [Trait("Rule", "silentscan/index-design/float-or-real-index-key-column")]
    public async Task FloatIndexKeyEquality_MissesSumOfTenthsWhileDecimalControlMatches()
    {
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Fl WITH (INDEX(IX_Fl_F)) WHERE F = 0.3;"));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Fl WITH (INDEX(IX_Fl_Dc)) WHERE Dc = 0.3;"));
    }

    [Fact]
    [Trait("Rule", "silentscan/query/distinct-masking-join-fanout")]
    public async Task JoinOnNonUniqueColumnFansOutAndDistinctHidesIt_UniqueControlDoesNotFanOut()
    {
        var fanned = await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Pa a JOIN dbo.Pb b ON b.PaId = a.Id;");
        var distinct = await ScalarAsync<int>("SELECT COUNT(*) FROM (SELECT DISTINCT a.Id FROM dbo.Pa a JOIN dbo.Pb b ON b.PaId = a.Id) q;");
        var uniqueJoin = await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Pa a JOIN dbo.Pu u ON u.PaId = a.Id;");

        Assert.Equal(3, fanned);
        Assert.Equal(2, distinct);
        Assert.Equal(2, uniqueJoin);
    }
}
