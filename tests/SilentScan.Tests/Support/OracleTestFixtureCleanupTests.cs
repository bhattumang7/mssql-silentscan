using Microsoft.Data.SqlClient;
using SilentScan.Verify;

namespace SilentScan.Tests.Support;

public sealed class OracleTestFixtureCleanupTests
{
    [Fact]
    public async Task InitializeAsync_WhenDeploymentFails_DropsTheProvisionedDatabase()
    {
        var fixture = new FailingDeploymentFixture();

        await Assert.ThrowsAnyAsync<Exception>(fixture.InitializeAsync);

        Assert.False(await DatabaseExistsAsync(fixture.ProvisionedName));
    }

    [Fact]
    public async Task InitializeAsync_WhenDeploymentSucceeds_KeepsTheDatabaseUntilDisposed()
    {
        var fixture = new SucceedingDeploymentFixture();

        await fixture.InitializeAsync();
        try
        {
            Assert.True(await DatabaseExistsAsync(fixture.ProvisionedName));
        }
        finally
        {
            await fixture.DisposeAsync();
        }

        Assert.False(await DatabaseExistsAsync(fixture.ProvisionedName));
    }

    private static async Task<bool> DatabaseExistsAsync(string databaseName)
    {
        await using var connection = new SqlConnection(SqlServerOptions.LocalDocker.BuildConnectionString(database: null));
        await connection.OpenAsync();
        await using var command = new SqlCommand("SELECT DB_ID(@name);", connection);
        command.Parameters.AddWithValue("@name", databaseName);
        return await command.ExecuteScalarAsync() is not (null or DBNull);
    }

    private sealed class FailingDeploymentFixture : OracleTestFixture
    {
        public string ProvisionedName => DatabaseName;

        protected override string DatabaseNameSeed => nameof(FailingDeploymentFixture);

        protected override string Ddl => "CREATE TABLE dbo.Broken (Id NOT_A_TYPE);";
    }

    private sealed class SucceedingDeploymentFixture : OracleTestFixture
    {
        public string ProvisionedName => DatabaseName;

        protected override string DatabaseNameSeed => nameof(SucceedingDeploymentFixture);

        protected override string Ddl => "CREATE TABLE dbo.Fine (Id INT NOT NULL);";
    }
}
