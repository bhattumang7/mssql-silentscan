using Microsoft.Data.SqlClient;
using SilentScan.Verify;
using SilentScan.Verify.Catalog;
using SilentScan.Verify.Deployment;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.CatchAllPredicateRuleId)]
public sealed class LiveCatalogReaderOptionalParameterOptimizationOracleTests : IAsyncLifetime
{
    private static readonly SqlServerOptions Options = SqlServerOptions.LocalDockerLatest;

    private readonly string _databaseName = $"{nameof(LiveCatalogReaderOptionalParameterOptimizationOracleTests)}_{Guid.NewGuid():N}";

    public async Task InitializeAsync() =>
        await new DatabaseProvisioner(Options).CreateFreshAsync(_databaseName);

    public async Task DisposeAsync() =>
        await new DatabaseProvisioner(Options).DropIfExistsAsync(_databaseName);

    [Fact]
    public async Task ReadAsync_ReportsTheDatabaseScopedOptimizationAsEnabledByDefaultAndDisabledOnceSwitchedOff()
    {
        var before = await new LiveCatalogReader(Options.BuildConnectionString(_databaseName)).ReadAsync();

        await using (var connection = new SqlConnection(Options.BuildConnectionString(_databaseName)))
        {
            await connection.OpenAsync();
            await using var off = new SqlCommand("ALTER DATABASE SCOPED CONFIGURATION SET OPTIONAL_PARAMETER_OPTIMIZATION = OFF;", connection);
            await off.ExecuteNonQueryAsync();
        }

        var after = await new LiveCatalogReader(Options.BuildConnectionString(_databaseName)).ReadAsync();

        Assert.False(before.IsOptionalParameterOptimizationDisabled);
        Assert.True(after.IsOptionalParameterOptimizationDisabled);
    }
}
