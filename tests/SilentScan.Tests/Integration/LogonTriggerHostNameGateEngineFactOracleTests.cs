using Microsoft.Data.SqlClient;
using SilentScan.Verify;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class LogonTriggerHostNameGateEngineFactOracleTests
{
    private static readonly SqlServerOptions Options = SqlServerOptions.LocalDocker;

    [Fact]
    public async Task HostName_ReturnsWhateverWorkstationIdTheClientClaimedInTheConnectionString()
    {
        var builder = new SqlConnectionStringBuilder(Options.BuildConnectionString())
        {
            WorkstationID = "AnyClaimedNameAtAll",
        };

        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT HOST_NAME();";

        var hostName = (string)(await command.ExecuteScalarAsync())!;
        Assert.Equal("AnyClaimedNameAtAll", hostName);
    }
}
