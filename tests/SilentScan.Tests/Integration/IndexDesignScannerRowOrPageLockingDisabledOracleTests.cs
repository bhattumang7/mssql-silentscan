using Microsoft.Data.SqlClient;
using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;
using SilentScan.Verify.Catalog;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.IndexDesignRowOrPageLockingDisabledRuleId)]
public sealed class IndexDesignScannerRowOrPageLockingDisabledOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(IndexDesignScannerRowOrPageLockingDisabledOracleTests);

    protected override string Ddl => """
        CREATE TABLE dbo.T1 (Id INT NOT NULL PRIMARY KEY, A INT NOT NULL, B INT NOT NULL, C INT NOT NULL, D INT NOT NULL);
        GO
        CREATE INDEX IX_T1_RowLocksOff ON dbo.T1 (A) WITH (ALLOW_ROW_LOCKS = OFF);
        GO
        CREATE INDEX IX_T1_Default ON dbo.T1 (B);
        GO
        CREATE INDEX IX_T1_PageLocksOffViaAlter ON dbo.T1 (C);
        GO
        ALTER INDEX IX_T1_PageLocksOffViaAlter ON dbo.T1 SET (ALLOW_PAGE_LOCKS = OFF);
        GO
        CREATE INDEX IX_T1_DisabledRowLocksOff ON dbo.T1 (D) WITH (ALLOW_ROW_LOCKS = OFF);
        GO
        ALTER INDEX IX_T1_DisabledRowLocksOff ON dbo.T1 DISABLE;
        GO
        CREATE TABLE dbo.LkOff (Id INT NOT NULL, V INT NOT NULL, CONSTRAINT PK_LkOff PRIMARY KEY CLUSTERED (Id) WITH (ALLOW_ROW_LOCKS = OFF));
        CREATE TABLE dbo.LkOn (Id INT NOT NULL, V INT NOT NULL, CONSTRAINT PK_LkOn PRIMARY KEY CLUSTERED (Id));
        INSERT INTO dbo.LkOff SELECT TOP (500) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)), 1 FROM sys.all_objects;
        INSERT INTO dbo.LkOn SELECT Id, V FROM dbo.LkOff;
        GO
        """;

    [Fact]
    public async Task ReadAsync_IndexLockOptions_MatchRealCatalog()
    {
        var catalog = await new LiveCatalogReader(Options.BuildConnectionString(DatabaseName)).ReadAsync();
        var table = Assert.Single(catalog.Tables, t => string.Equals(t.QualifiedName, "dbo.T1", StringComparison.OrdinalIgnoreCase));

        await using var connection = new SqlConnection(Options.BuildConnectionString(DatabaseName));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT i.name, i.allow_row_locks, i.allow_page_locks
            FROM sys.indexes i
            JOIN sys.tables t ON t.object_id = i.object_id
            WHERE t.name = 'T1' AND i.name IS NOT NULL;
            """;

        var realByName = new Dictionary<string, (bool AllowRowLocks, bool AllowPageLocks)>(StringComparer.OrdinalIgnoreCase);
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                realByName[reader.GetString(0)] = (reader.GetBoolean(1), reader.GetBoolean(2));
            }
        }

        Assert.False(realByName["IX_T1_RowLocksOff"].AllowRowLocks);
        Assert.True(realByName["IX_T1_RowLocksOff"].AllowPageLocks);
        Assert.False(realByName["IX_T1_PageLocksOffViaAlter"].AllowPageLocks);
        Assert.True(realByName["IX_T1_PageLocksOffViaAlter"].AllowRowLocks);
        Assert.True(realByName["IX_T1_Default"].AllowRowLocks);
        Assert.True(realByName["IX_T1_Default"].AllowPageLocks);

        foreach (var (name, real) in realByName)
        {
            var index = Assert.Single(table.Indexes, i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase));
            Assert.Equal(real.AllowRowLocks, index.AllowRowLocks);
            Assert.Equal(real.AllowPageLocks, index.AllowPageLocks);
        }
    }

    [Fact]
    public async Task Scan_IndexesWithLockGranularityDisabled_ReportsOnlyAffectedActiveIndexes()
    {
        var catalog = await new LiveCatalogReader(Options.BuildConnectionString(DatabaseName)).ReadAsync();

        var findings = IndexDesignScanner.Scan(catalog);
        var lockFindings = findings.Where(f => f.Kind == IndexDesignFindingKind.RowOrPageLockingDisabled).ToList();

        var flaggedIndexNames = lockFindings.Select(f => f.IndexName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("IX_T1_RowLocksOff", flaggedIndexNames);
        Assert.Contains("IX_T1_PageLocksOffViaAlter", flaggedIndexNames);
        Assert.DoesNotContain("IX_T1_Default", flaggedIndexNames);
        Assert.DoesNotContain("IX_T1_DisabledRowLocksOff", flaggedIndexNames);

        var rowLocksFinding = Assert.Single(lockFindings, f => string.Equals(f.IndexName, "IX_T1_RowLocksOff", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("ALLOW_ROW_LOCKS", rowLocksFinding.DetailText);
        Assert.DoesNotContain("ALLOW_PAGE_LOCKS", rowLocksFinding.DetailText);

        var pageLocksFinding = Assert.Single(lockFindings, f => string.Equals(f.IndexName, "IX_T1_PageLocksOffViaAlter", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("ALLOW_PAGE_LOCKS", pageLocksFinding.DetailText);
        Assert.DoesNotContain("ALLOW_ROW_LOCKS", pageLocksFinding.DetailText);
    }

    private async Task<IReadOnlyList<string>> ReadLockResourceTypesAfterSingleRowUpdateAsync(string table)
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(DatabaseName));
        await connection.OpenAsync();

        await using (var begin = new SqlCommand($"BEGIN TRANSACTION; UPDATE dbo.{table} SET V = 2 WHERE Id = 5;", connection))
        {
            await begin.ExecuteNonQueryAsync();
        }

        var resourceTypes = new List<string>();
        await using (var read = new SqlCommand(
            "SELECT DISTINCT resource_type FROM sys.dm_tran_locks WHERE request_session_id = @@SPID AND resource_type IN ('KEY', 'PAGE', 'RID') AND request_mode IN ('X', 'U');",
            connection))
        await using (var reader = await read.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                resourceTypes.Add(reader.GetString(0));
            }
        }

        await using (var rollback = new SqlCommand("ROLLBACK TRANSACTION;", connection))
        {
            await rollback.ExecuteNonQueryAsync();
        }

        return resourceTypes;
    }

    [Fact]
    public async Task SingleRowUpdate_WithRowLocksDisabled_TakesAPageLockWhereTheDefaultTakesAKeyLock()
    {
        Assert.Equal(["PAGE"], await ReadLockResourceTypesAfterSingleRowUpdateAsync("LkOff"));
        Assert.Equal(["KEY"], await ReadLockResourceTypesAfterSingleRowUpdateAsync("LkOn"));
    }
}
