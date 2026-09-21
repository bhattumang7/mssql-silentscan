using Microsoft.Data.SqlClient;
using SilentScan.Verify;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
[Trait("Rule", "silentscan/trigger/logon-trigger-host-name-gate")]
public sealed class LogonTriggerHostNameGateEngineFactOracleTests
{
    private static readonly SqlServerOptions Options = SqlServerOptions.LocalDocker;

    private static async Task<(string HostName, string ClientNetAddress)> ConnectClaimingAsync(string workstationId)
    {
        var builder = new SqlConnectionStringBuilder(Options.BuildConnectionString())
        {
            WorkstationID = workstationId,
        };

        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT HOST_NAME(), CAST(client_net_address AS NVARCHAR(48)) FROM sys.dm_exec_connections WHERE session_id = @@SPID;";

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetString(0), reader.GetString(1));
    }

    [Fact]
    public async Task HostName_ReturnsWhateverWorkstationIdTheClientClaimed_ServerObservedClientAddressControlDoesNotChange()
    {
        var first = await ConnectClaimingAsync("AnyClaimedNameAtAll");
        var second = await ConnectClaimingAsync("ADifferentClaimedName");

        Assert.Equal("AnyClaimedNameAtAll", first.HostName);
        Assert.Equal("ADifferentClaimedName", second.HostName);
        Assert.Equal(first.ClientNetAddress, second.ClientNetAddress);
    }
}
