using Microsoft.Data.SqlClient;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.QueryAntiPatternMergeMissingHoldlockRuleId)]
public sealed class MergeMissingHoldlockEngineFactOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(MergeMissingHoldlockEngineFactOracleTests);

    protected override string Ddl => """
        CREATE TABLE dbo.Inventory (Sku VARCHAR(20) NOT NULL PRIMARY KEY, Quantity INT NOT NULL);
        CREATE TABLE dbo.KeyedTarget (K INT NOT NULL PRIMARY KEY, V INT NOT NULL);
        CREATE TABLE dbo.HeapTarget (K INT NOT NULL, V INT NOT NULL);
        CREATE TABLE dbo.HeapTargetLocked (K INT NOT NULL, V INT NOT NULL);
        CREATE TABLE dbo.HeapTargetUpdlock (K INT NOT NULL, V INT NOT NULL);
        CREATE TABLE dbo.HeapTargetTabLock (K INT NOT NULL, V INT NOT NULL);
        CREATE TABLE dbo.HeapTargetSerializable (K INT NOT NULL, V INT NOT NULL);
        CREATE TABLE dbo.ConstantFalseTarget (K INT NOT NULL, V INT NOT NULL);
        CREATE TABLE dbo.ConstantFalseTargetLocked (K INT NOT NULL, V INT NOT NULL);
        GO
        """;

    private async Task<bool> KeyLockStillHeldMidTransactionAsync(string targetHint)
    {
        await using var holderConnection = new SqlConnection(Options.BuildConnectionString(DatabaseName));
        await holderConnection.OpenAsync();

        int spid;
        await using (var spidCommand = holderConnection.CreateCommand())
        {
            spidCommand.CommandText = "SELECT @@SPID;";
            spid = (short)(await spidCommand.ExecuteScalarAsync())!;
        }

        await using (var beginCommand = holderConnection.CreateCommand())
        {
            beginCommand.CommandText = "BEGIN TRANSACTION;";
            await beginCommand.ExecuteNonQueryAsync();
        }

        await using (var checkCommand = holderConnection.CreateCommand())
        {
            checkCommand.CommandText = $"SELECT Quantity FROM dbo.Inventory {targetHint} WHERE Sku = '42';";
            await checkCommand.ExecuteScalarAsync();
        }

        bool keyLockHeld;
        await using (var observerConnection = new SqlConnection(Options.BuildConnectionString(DatabaseName)))
        {
            await observerConnection.OpenAsync();
            await using var lockCommand = observerConnection.CreateCommand();
            lockCommand.CommandText = """
                SELECT COUNT(*) FROM sys.dm_tran_locks
                WHERE request_session_id = @spid AND resource_type = 'KEY' AND request_status = 'GRANT';
                """;
            lockCommand.Parameters.AddWithValue("@spid", spid);
            var lockCount = (int)(await lockCommand.ExecuteScalarAsync())!;
            keyLockHeld = lockCount > 0;
        }

        await using (var commitCommand = holderConnection.CreateCommand())
        {
            commitCommand.CommandText = "COMMIT TRANSACTION;";
            await commitCommand.ExecuteNonQueryAsync();
        }

        return keyLockHeld;
    }

    [Fact]
    public async Task ExistenceCheckWithoutHoldlock_ReleasesItsKeyLockBeforeTheTransactionCommits()
    {
        Assert.False(await KeyLockStillHeldMidTransactionAsync(targetHint: string.Empty));
    }

    [Fact]
    public async Task ExistenceCheckWithHoldlock_KeepsItsKeyLockGrantedUntilTheTransactionCommits()
    {
        Assert.True(await KeyLockStillHeldMidTransactionAsync(targetHint: "WITH (HOLDLOCK)"));
    }

    private async Task<(int DuplicateKeys, int Errors)> RaceAsync(
        string table, string hint, string isolationLevel = "READ COMMITTED", string matchCondition = "t.K = s.K")
    {
        const int Sessions = 6;
        const int Keys = 300;
        var errors = 0;
        var barriers = Enumerable.Range(0, Keys).Select(_ => new Barrier(Sessions)).ToArray();

        await Task.WhenAll(Enumerable.Range(0, Sessions).Select(_ => Task.Run(async () =>
        {
            await using var connection = new SqlConnection(Options.BuildConnectionString(DatabaseName));
            await connection.OpenAsync();
            await using (var isolation = new SqlCommand($"SET TRANSACTION ISOLATION LEVEL {isolationLevel};", connection))
            {
                await isolation.ExecuteNonQueryAsync();
            }

            for (var key = 0; key < Keys; key++)
            {
                barriers[key].SignalAndWait();
                await using var command = new SqlCommand(
                    $"MERGE dbo.{table} {hint} AS t USING (VALUES (@k, 1)) AS s (K, V) ON {matchCondition} WHEN NOT MATCHED THEN INSERT (K, V) VALUES (s.K, s.V);",
                    connection);
                command.Parameters.AddWithValue("@k", key);
                try
                {
                    await command.ExecuteNonQueryAsync();
                }
                catch (SqlException)
                {
                    Interlocked.Increment(ref errors);
                }
            }
        })));

        await using var verify = new SqlConnection(Options.BuildConnectionString(DatabaseName));
        await verify.OpenAsync();
        await using var count = new SqlCommand($"SELECT COUNT(*) FROM (SELECT K FROM dbo.{table} GROUP BY K HAVING COUNT(*) > 1) d;", verify);
        return ((int)(await count.ExecuteScalarAsync())!, errors);
    }

    [Fact]
    public async Task ConcurrentMergeWithoutHoldlock_OnAHeapTarget_SilentlyInsertsDuplicateKeys()
    {
        var (duplicateKeys, errors) = await RaceAsync("HeapTarget", string.Empty);

        Assert.True(duplicateKeys > 0, $"duplicate keys {duplicateKeys}, errors {errors}");
        Assert.Equal(0, errors);
    }

    [Fact]
    public async Task ConcurrentMergeWithOnlyAnUpdlockHint_OnAHeapTarget_StillInsertsDuplicateKeys()
    {
        var (duplicateKeys, _) = await RaceAsync("HeapTargetUpdlock", "WITH (UPDLOCK)");

        Assert.True(duplicateKeys > 0, $"duplicate keys {duplicateKeys}");
    }

    [Fact]
    public async Task ConcurrentMergeWithTabLock_OnAHeapTarget_InsertsNoDuplicateKeys()
    {
        var (duplicateKeys, _) = await RaceAsync("HeapTargetTabLock", "WITH (TABLOCK)");

        Assert.Equal(0, duplicateKeys);
    }

    [Fact]
    public async Task ConcurrentMergeWithHoldlock_OnAHeapTarget_InsertsNoDuplicateKeys()
    {
        var (duplicateKeys, _) = await RaceAsync("HeapTargetLocked", "WITH (HOLDLOCK)");

        Assert.Equal(0, duplicateKeys);
    }

    [Fact]
    public async Task ConcurrentMergeWithAConstantFalseMatchCondition_InsertsEveryRow_WithOrWithoutHoldlock()
    {
        var (withoutHint, _) = await RaceAsync("ConstantFalseTarget", string.Empty, matchCondition: "1 = 0");
        var (withHoldlock, _) = await RaceAsync("ConstantFalseTargetLocked", "WITH (HOLDLOCK)", matchCondition: "1 = 0");

        Assert.Equal(withoutHint, withHoldlock);
        Assert.True(withHoldlock > 0, $"duplicate keys {withHoldlock}");
    }

    [Fact]
    public async Task ConcurrentMergeWithoutHints_UnderSerializableIsolation_OnAHeapTarget_InsertsNoDuplicateKeys()
    {
        var (duplicateKeys, _) = await RaceAsync("HeapTargetSerializable", string.Empty, "SERIALIZABLE");

        Assert.Equal(0, duplicateKeys);
    }

    [Fact]
    public async Task ConcurrentMergeWithoutHoldlock_OnAKeyedTarget_FailsLoudlyInsteadOfDuplicating()
    {
        var (duplicateKeys, errors) = await RaceAsync("KeyedTarget", string.Empty);

        Assert.Equal(0, duplicateKeys);
        Assert.True(errors > 0);
    }
}
