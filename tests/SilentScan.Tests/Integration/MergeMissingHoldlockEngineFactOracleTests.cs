using Microsoft.Data.SqlClient;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class MergeMissingHoldlockEngineFactOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(MergeMissingHoldlockEngineFactOracleTests);

    protected override string Ddl => """
        CREATE TABLE dbo.Inventory (Sku VARCHAR(20) NOT NULL PRIMARY KEY, Quantity INT NOT NULL);
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
}
