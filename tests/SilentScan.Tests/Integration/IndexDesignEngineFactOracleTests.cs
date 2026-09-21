using SilentScan.Tests.Support;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class IndexDesignEngineFactOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(IndexDesignEngineFactOracleTests);

    protected override string Ddl => """
        CREATE TABLE dbo.ParNo (Id INT PRIMARY KEY);
        CREATE TABLE dbo.ChdNo (Id INT IDENTITY PRIMARY KEY, ParId INT NOT NULL CONSTRAINT FK_ChdNo FOREIGN KEY REFERENCES dbo.ParNo(Id), Pad CHAR(100) DEFAULT 'x');
        CREATE TABLE dbo.ParYes (Id INT PRIMARY KEY);
        CREATE TABLE dbo.ChdYes (Id INT IDENTITY PRIMARY KEY, ParId INT NOT NULL CONSTRAINT FK_ChdYes FOREIGN KEY REFERENCES dbo.ParYes(Id), Pad CHAR(100) DEFAULT 'x');
        INSERT INTO dbo.ParNo SELECT TOP (1000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) FROM sys.all_objects a, sys.all_objects b;
        INSERT INTO dbo.ParYes SELECT Id FROM dbo.ParNo;
        INSERT INTO dbo.ChdNo (ParId) SELECT TOP (20000) (ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) % 1000) + 1 FROM sys.all_objects a, sys.all_objects b;
        INSERT INTO dbo.ChdYes (ParId) SELECT ParId FROM dbo.ChdNo;
        CREATE INDEX IX_ChdYes_ParId ON dbo.ChdYes(ParId);
        GO
        CREATE TABLE dbo.Fi (Id INT PRIMARY KEY, Status INT NOT NULL, Other INT NOT NULL);
        INSERT INTO dbo.Fi SELECT TOP (5000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)), ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) % 10, 1 FROM sys.all_objects a, sys.all_objects b;
        CREATE INDEX IX_Fi_Other ON dbo.Fi(Other) WHERE Status = 1;
        CREATE INDEX IX_Fi_OtherS ON dbo.Fi(Other) INCLUDE (Status) WHERE Status = 1;
        GO
        CREATE TABLE dbo.Mg (K INT NOT NULL, A INT NOT NULL, B INT NOT NULL, Id INT PRIMARY KEY);
        INSERT INTO dbo.Mg SELECT TOP (5000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) % 500, 1, 1, ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) FROM sys.all_objects a, sys.all_objects b;
        CREATE INDEX IX_Mg_A ON dbo.Mg(K) INCLUDE (A);
        CREATE INDEX IX_Mg_B ON dbo.Mg(K) INCLUDE (B);
        CREATE INDEX IX_Mg_Both ON dbo.Mg(K) INCLUDE (A, B);
        GO
        CREATE TABLE dbo.SbWide (K INT NOT NULL, L INT NOT NULL, Id INT PRIMARY KEY);
        INSERT INTO dbo.SbWide SELECT TOP (5000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) % 500, 1, ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) FROM sys.all_objects a, sys.all_objects b;
        CREATE INDEX IX_SbWide ON dbo.SbWide(K, L);
        CREATE TABLE dbo.SbTrailing (K INT NOT NULL, L INT NOT NULL, Id INT PRIMARY KEY);
        INSERT INTO dbo.SbTrailing SELECT K, L, Id FROM dbo.SbWide;
        CREATE INDEX IX_SbTrailing ON dbo.SbTrailing(L, K);
        GO
        CREATE TABLE dbo.U (K INT, V INT);
        INSERT INTO dbo.U SELECT TOP (3000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) % 2, ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) FROM sys.all_objects a, sys.all_objects b;
        GO
        CREATE TABLE dbo.Hy (Id INT PRIMARY KEY, K INT);
        INSERT INTO dbo.Hy VALUES (1, 1), (2, 2);
        CREATE INDEX IX_Hyp ON dbo.Hy(K) WITH STATISTICS_ONLY = -1;
        CREATE INDEX IX_Real ON dbo.Hy(K, Id);
        GO
        CREATE TABLE dbo.Di (A INT, B INT);
        CREATE INDEX IX_Di ON dbo.Di(A);
        ALTER INDEX IX_Di ON dbo.Di DISABLE;
        GO
        CREATE TABLE dbo.Du (A INT, B INT);
        CREATE INDEX IX_Du1 ON dbo.Du(A);
        CREATE INDEX IX_Du2 ON dbo.Du(A);
        CREATE INDEX IX_Du3 ON dbo.Du(A) WHERE A > 500;
        GO
        CREATE TABLE dbo.NuClustered (K INT NOT NULL, Id INT NOT NULL);
        CREATE CLUSTERED INDEX CX ON dbo.NuClustered(K);
        CREATE INDEX NX ON dbo.NuClustered(Id);
        CREATE TABLE dbo.UqClustered (K INT NOT NULL, Id INT NOT NULL);
        CREATE UNIQUE CLUSTERED INDEX CX ON dbo.UqClustered(K);
        CREATE INDEX NX ON dbo.UqClustered(Id);
        INSERT INTO dbo.NuClustered SELECT TOP (5000) 1, ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) FROM sys.all_objects a, sys.all_objects b;
        INSERT INTO dbo.UqClustered SELECT TOP (5000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)), ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) FROM sys.all_objects a, sys.all_objects b;
        GO
        CREATE TABLE dbo.Hp (Id INT, Pad VARCHAR(4000) NULL);
        INSERT INTO dbo.Hp SELECT TOP (200) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)), '' FROM sys.all_objects;
        CREATE NONCLUSTERED INDEX IX_Hp ON dbo.Hp(Id);
        CREATE TABLE dbo.Cl (Id INT PRIMARY KEY, Pad VARCHAR(4000) NULL);
        INSERT INTO dbo.Cl SELECT TOP (200) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)), '' FROM sys.all_objects;
        GO
        CREATE TABLE dbo.PkNonClustered (Id INT NOT NULL, Pad VARCHAR(10), CONSTRAINT PK_PkNonClustered PRIMARY KEY NONCLUSTERED (Id));
        CREATE TABLE dbo.PkClustered (Id INT NOT NULL, Pad VARCHAR(10), CONSTRAINT PK_PkClustered PRIMARY KEY CLUSTERED (Id));
        GO
        CREATE TABLE dbo.IdentityKey (Id INT IDENTITY(1,1) NOT NULL, Pad CHAR(500) NOT NULL DEFAULT 'x', CONSTRAINT PK_IdentityKey PRIMARY KEY CLUSTERED (Id));
        CREATE TABLE dbo.SequentialKey (Id INT IDENTITY(1,1) NOT NULL, Pad CHAR(500) NOT NULL DEFAULT 'x', CONSTRAINT PK_SequentialKey PRIMARY KEY CLUSTERED (Id) WITH (OPTIMIZE_FOR_SEQUENTIAL_KEY = ON));
        CREATE TABLE dbo.GuidKey (Id UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(), Pad CHAR(500) NOT NULL DEFAULT 'x', CONSTRAINT PK_GuidKey PRIMARY KEY CLUSTERED (Id));
        DECLARE @i INT = 0;
        WHILE @i < 300
        BEGIN
            INSERT dbo.IdentityKey DEFAULT VALUES;
            INSERT dbo.GuidKey DEFAULT VALUES;
            SET @i += 1;
        END
        GO
        CREATE PARTITION FUNCTION pf(INT) AS RANGE LEFT FOR VALUES (100, 200);
        CREATE PARTITION SCHEME ps AS PARTITION pf ALL TO ([PRIMARY]);
        CREATE TABLE dbo.Pt (Id INT NOT NULL, V INT NOT NULL) ON ps(Id);
        CREATE TABLE dbo.Pstage (Id INT NOT NULL, V INT NOT NULL) ON [PRIMARY];
        ALTER TABLE dbo.Pstage ADD CONSTRAINT CK_stage CHECK (Id <= 100);
        INSERT INTO dbo.Pt VALUES (1, 1);
        CREATE INDEX IX_Pt_V ON dbo.Pt(V) ON [PRIMARY];
        GO
        """;

    private static bool Seeks(System.Xml.Linq.XDocument plan, string table) =>
        ShowPlan.PhysicalOpsOnTable(plan, table).Contains("Index Seek");

    [Fact]
    [Trait("Rule", "silentscan/index-design/unindexed-foreign-key")]
    public async Task ParentDelete_ScansUnindexedChild_SeeksIndexedChild()
    {
        var unindexed = await PlanInSessionAsync(string.Empty, "DELETE FROM dbo.ParNo WHERE Id = 5;");
        var indexed = await PlanInSessionAsync(string.Empty, "DELETE FROM dbo.ParYes WHERE Id = 5;");

        Assert.Contains("Clustered Index Scan", ShowPlan.PhysicalOpsOnTable(unindexed, "ChdNo"));
        Assert.False(Seeks(unindexed, "ChdNo"));
        Assert.True(Seeks(indexed, "ChdYes"));
        Assert.Contains("IX_ChdYes_ParId", ShowPlan.IndexNames(indexed));
    }

    [Fact]
    [Trait("Rule", "silentscan/index-design/filter-column-not-in-index")]
    public async Task FilteredIndexWithoutFilterColumn_NeedsKeyLookup_IncludeControlDoesNot()
    {
        var missing = await PlanInSessionAsync(string.Empty, "SELECT Status FROM dbo.Fi WITH (INDEX(IX_Fi_Other)) WHERE Status = 1 AND Other = 1;");
        var included = await PlanInSessionAsync(string.Empty, "SELECT Status FROM dbo.Fi WITH (INDEX(IX_Fi_OtherS)) WHERE Status = 1 AND Other = 1;");

        Assert.True(ShowPlan.HasKeyLookup(missing));
        Assert.False(ShowPlan.HasKeyLookup(included));
    }

    [Fact]
    [Trait("Rule", "silentscan/index-design/mergeable-indexes-differing-include-only")]
    public async Task MergedIndexServesBothOriginalQueriesWithoutLookup_SingleOriginalCannot()
    {
        var forA = await PlanInSessionAsync(string.Empty, "SELECT A FROM dbo.Mg WITH (INDEX(IX_Mg_Both)) WHERE K = 7;");
        var forB = await PlanInSessionAsync(string.Empty, "SELECT B FROM dbo.Mg WITH (INDEX(IX_Mg_Both)) WHERE K = 7;");
        var originalCannot = await PlanInSessionAsync(string.Empty, "SELECT A, B FROM dbo.Mg WITH (INDEX(IX_Mg_A)) WHERE K = 7;");

        Assert.True(Seeks(forA, "Mg"));
        Assert.False(ShowPlan.HasKeyLookup(forA));
        Assert.True(Seeks(forB, "Mg"));
        Assert.False(ShowPlan.HasKeyLookup(forB));
        Assert.True(ShowPlan.HasKeyLookup(originalCannot));
    }

    [Fact]
    [Trait("Rule", "silentscan/index-design/subsumed-index")]
    public async Task WiderIndexServesNarrowLeadingKeySeek_TrailingOnlyControlCannotSeek()
    {
        var wide = await PlanInSessionAsync(string.Empty, "SELECT K FROM dbo.SbWide WHERE K = 7;");
        var trailing = await PlanInSessionAsync(string.Empty, "SELECT K FROM dbo.SbTrailing WHERE K = 7;");

        Assert.Contains("IX_SbWide", ShowPlan.IndexNames(wide));
        Assert.True(Seeks(wide, "SbWide"));
        Assert.False(ShowPlan.HasKeyLookup(wide));
        Assert.False(Seeks(trailing, "SbTrailing"));
    }

    [Fact]
    [Trait("Rule", "silentscan/index-design/hypothetical-index")]
    public async Task HypotheticalIndex_IsFlaggedAndCannotBeUsed_RealIndexControlCan()
    {
        var flags = await RowsAsync("SELECT name, CAST(is_hypothetical AS INT) FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.Hy') AND name IN ('IX_Hyp', 'IX_Real');");

        Assert.Equal(1, flags.Single(r => (string)r[0]! == "IX_Hyp")[1]);
        Assert.Equal(0, flags.Single(r => (string)r[0]! == "IX_Real")[1]);
        Assert.Equal(308, await SqlErrorNumberAsync("SELECT K FROM dbo.Hy WITH (INDEX(IX_Hyp)) WHERE K = 1;"));
        Assert.Null(await SqlErrorNumberAsync("SELECT K FROM dbo.Hy WITH (INDEX(IX_Real)) WHERE K = 1;"));
    }

    [Fact]
    [Trait("Rule", "silentscan/index-design/disabled-index")]
    public async Task DisabledIndex_BlocksSameNameCreateAndCannotBeHinted_RebuildRestoresIt()
    {
        Assert.Equal(1913, await SqlErrorNumberAsync("CREATE INDEX IX_Di ON dbo.Di(B);"));
        Assert.Equal(315, await SqlErrorNumberAsync("SELECT A FROM dbo.Di WITH (INDEX(IX_Di)) WHERE A = 1;"));

        await ExecuteAsync("ALTER INDEX IX_Di ON dbo.Di REBUILD;");

        Assert.Null(await SqlErrorNumberAsync("SELECT A FROM dbo.Di WITH (INDEX(IX_Di)) WHERE A = 1;"));
    }

    [Fact]
    [Trait("Rule", "silentscan/index-design/duplicate-index")]
    public async Task DuplicateIndexes_EachReceiveEveryInsert_FilteredControlReceivesFewer()
    {
        await ExecuteAsync("INSERT INTO dbo.Du SELECT TOP (1000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)), 1 FROM sys.all_objects a, sys.all_objects b;");

        var rows = await RowsAsync("SELECT i.name, SUM(s.leaf_insert_count) FROM sys.dm_db_index_operational_stats(DB_ID(), OBJECT_ID('dbo.Du'), NULL, NULL) s JOIN sys.indexes i ON i.object_id = s.object_id AND i.index_id = s.index_id WHERE i.name LIKE 'IX_Du%' GROUP BY i.name;");

        Assert.Equal(1000L, Convert.ToInt64(rows.Single(r => (string)r[0]! == "IX_Du1")[1], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(1000L, Convert.ToInt64(rows.Single(r => (string)r[0]! == "IX_Du2")[1], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(500L, Convert.ToInt64(rows.Single(r => (string)r[0]! == "IX_Du3")[1], System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    [Trait("Rule", "silentscan/index-design/non-unique-clustered-index")]
    public async Task NonUniqueClusteredKey_WidensNonclusteredLeafRecords()
    {
        const string sizeSql = "SELECT avg_record_size_in_bytes FROM sys.dm_db_index_physical_stats(DB_ID(), OBJECT_ID('{0}'), 2, NULL, 'DETAILED') WHERE index_level = 0;";

        var nonUnique = await ScalarAsync<double>(sizeSql.Replace("{0}", "dbo.NuClustered", StringComparison.Ordinal));
        var unique = await ScalarAsync<double>(sizeSql.Replace("{0}", "dbo.UqClustered", StringComparison.Ordinal));

        Assert.True(nonUnique > unique, $"{nonUnique} vs {unique}");
    }

    [Fact]
    [Trait("Rule", "silentscan/index-design/heap-with-nonclustered-indexes")]
    public async Task HeapRowsForwardAndChangeLocationWhenRebuilt_ClusteredControlHasNeither()
    {
        await ExecuteAsync("UPDATE dbo.Hp SET Pad = REPLICATE('x', 3000); UPDATE dbo.Cl SET Pad = REPLICATE('x', 3000);");
        var forwarded = await ScalarAsync<long>("SELECT ISNULL(SUM(forwarded_record_count), 0) FROM sys.dm_db_index_physical_stats(DB_ID(), OBJECT_ID('dbo.Hp'), 0, NULL, 'DETAILED');");
        var clusteredForwarded = await ScalarAsync<long>("SELECT ISNULL(SUM(forwarded_record_count), 0) FROM sys.dm_db_index_physical_stats(DB_ID(), OBJECT_ID('dbo.Cl'), 1, NULL, 'DETAILED');");

        const string location = "SELECT sys.fn_PhysLocFormatter(%%physloc%%) FROM dbo.Hp WHERE Id = 150;";
        var before = await ScalarAsync<string>(location);
        await ExecuteAsync("DELETE FROM dbo.Hp WHERE Id <= 100; ALTER TABLE dbo.Hp REBUILD;");
        var after = await ScalarAsync<string>(location);

        Assert.True(forwarded > 0);
        Assert.Equal(0, clusteredForwarded);
        Assert.NotEqual(before, after);
    }

    [Fact]
    [Trait("Rule", "silentscan/index-design/heap-with-nonclustered-primary-key")]
    public async Task NonclusteredPrimaryKey_LeavesAHeap_ClusteredPrimaryKeyControlDoesNot()
    {
        var types = await RowsAsync("SELECT OBJECT_NAME(object_id), type_desc FROM sys.indexes WHERE object_id IN (OBJECT_ID('dbo.PkNonClustered'), OBJECT_ID('dbo.PkClustered'));");

        Assert.Contains(types, r => (string)r[0]! == "PkNonClustered" && (string)r[1]! == "HEAP");
        Assert.DoesNotContain(types, r => (string)r[0]! == "PkClustered" && (string)r[1]! == "HEAP");
    }

    [Fact]
    [Trait("Rule", "silentscan/index-design/monotonic-clustered-key-missing-sequential-optimization")]
    public async Task IdentityClusteredKey_AppendsInOrderWithoutSequentialOptimization_OptionControlIsFlagged()
    {
        var backwards = await ScalarAsync<int>("SELECT COUNT(*) FROM (SELECT p.page_id, LAG(p.page_id) OVER (ORDER BY Id) AS prev FROM dbo.IdentityKey CROSS APPLY sys.fn_PhysLocCracker(%%physloc%%) p) x WHERE prev > page_id;");
        var flags = await RowsAsync("SELECT name, CAST(optimize_for_sequential_key AS INT) FROM sys.indexes WHERE name IN ('PK_IdentityKey', 'PK_SequentialKey');");

        Assert.Equal(0, backwards);
        Assert.Equal(0, flags.Single(r => (string)r[0]! == "PK_IdentityKey")[1]);
        Assert.Equal(1, flags.Single(r => (string)r[0]! == "PK_SequentialKey")[1]);
    }

    [Fact]
    [Trait("Rule", "silentscan/index-design/random-clustered-key-guid-default")]
    public async Task NewidClusteredKey_PlacesRowsOutOfInsertOrder_IdentityControlDoesNot()
    {
        const string backwardsSql = "SELECT COUNT(*) FROM (SELECT p.page_id, LAG(p.page_id) OVER (ORDER BY Id) AS prev FROM {0} CROSS APPLY sys.fn_PhysLocCracker(%%physloc%%) p) x WHERE prev > page_id;";

        Assert.True(await ScalarAsync<int>(backwardsSql.Replace("{0}", "dbo.GuidKey", StringComparison.Ordinal)) > 0);
        Assert.Equal(0, await ScalarAsync<int>(backwardsSql.Replace("{0}", "dbo.IdentityKey", StringComparison.Ordinal)));
    }

    [Fact]
    [Trait("Rule", "silentscan/index-design/non-aligned-partitioned-index")]
    public async Task NonAlignedIndex_BlocksPartitionSwitch_DroppingItAllowsIt()
    {
        var blocked = await SqlErrorNumberAsync("ALTER TABLE dbo.Pt SWITCH PARTITION 1 TO dbo.Pstage;");
        await ExecuteAsync("DROP INDEX IX_Pt_V ON dbo.Pt;");
        var allowed = await SqlErrorNumberAsync("ALTER TABLE dbo.Pt SWITCH PARTITION 1 TO dbo.Pstage;");

        Assert.NotNull(blocked);
        Assert.Null(allowed);
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Pstage;"));
    }
}
