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
    public async Task InitializeAsync_WhenSubclassSeedingFails_DropsTheProvisionedDatabase()
    {
        var fixture = new FailingSeedFixture();

        await Assert.ThrowsAnyAsync<InvalidOperationException>(fixture.InitializeAsync);

        Assert.False(await DatabaseExistsAsync(fixture.ProvisionedName));
    }

    [Fact]
    public async Task InitializeAsync_WhenSubclassSeedingSucceeds_SeedRunsAgainstTheDeployedDatabase()
    {
        var fixture = new SeededFixture();

        await fixture.InitializeAsync();
        try
        {
            Assert.Equal(1, await fixture.SeededRowCountAsync());
        }
        finally
        {
            await fixture.DisposeAsync();
        }
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

    private sealed class FailingSeedFixture : OracleTestFixture
    {
        public string ProvisionedName => DatabaseName;

        protected override string DatabaseNameSeed => nameof(FailingSeedFixture);

        protected override string Ddl => "CREATE TABLE dbo.Fine (Id INT NOT NULL);";

        protected override Task SeedAsync() => throw new InvalidOperationException("seed failed");
    }

    private sealed class SeededFixture : OracleTestFixture
    {
        protected override string DatabaseNameSeed => nameof(SeededFixture);

        protected override string Ddl => "CREATE TABLE dbo.Fine (Id INT NOT NULL);";

        public Task<int> SeededRowCountAsync() => ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Fine;");

        protected override Task SeedAsync() => ExecuteAsync("INSERT INTO dbo.Fine (Id) VALUES (1);");
    }

    private sealed class SucceedingDeploymentFixture : OracleTestFixture
    {
        public string ProvisionedName => DatabaseName;

        protected override string DatabaseNameSeed => nameof(SucceedingDeploymentFixture);

        protected override string Ddl => "CREATE TABLE dbo.Fine (Id INT NOT NULL);";
    }
}
