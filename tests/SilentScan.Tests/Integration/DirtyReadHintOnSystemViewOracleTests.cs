using Microsoft.Data.SqlClient;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.ControlFlowRiskDirtyReadIsolationHintRuleId)]
public sealed class DirtyReadHintOnSystemViewOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(DirtyReadHintOnSystemViewOracleTests);

    protected override string Ddl => "CREATE TABLE dbo.Anchor (Id INT NOT NULL PRIMARY KEY);";

    private async Task<(int? Count, bool TimedOut)> ReadWhileDdlIsUncommittedAsync(string tableHint)
    {
        await using var writer = await OpenConnectionAsync();
        await ExecuteAsync(writer, "BEGIN TRANSACTION; CREATE TABLE dbo.UncommittedProbe (a INT);");

        try
        {
            await using var reader = await OpenConnectionAsync();
            await using var command = new SqlCommand(
                $"SET LOCK_TIMEOUT 3000; SELECT COUNT(*) FROM sys.objects {tableHint} WHERE name = N'UncommittedProbe';", reader);
            return ((int)(await command.ExecuteScalarAsync())!, false);
        }
        catch (SqlException ex) when (ex.Number == 1222)
        {
            return (null, true);
        }
        finally
        {
            await ExecuteAsync(writer, "ROLLBACK TRANSACTION;");
        }
    }

    [Fact]
    public async Task NoLockOnACatalogView_ReadsUncommittedMetadata()
    {
        var (count, timedOut) = await ReadWhileDdlIsUncommittedAsync("WITH (NOLOCK)");

        Assert.False(timedOut);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task CatalogViewWithoutTheHint_WaitsForTheUncommittedDdl()
    {
        var (_, timedOut) = await ReadWhileDdlIsUncommittedAsync(string.Empty);

        Assert.True(timedOut);
    }
}
