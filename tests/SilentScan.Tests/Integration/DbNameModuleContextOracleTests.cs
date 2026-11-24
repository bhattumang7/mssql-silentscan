using Microsoft.Data.SqlClient;
using SilentScan.Tests.Support;
using SilentScan.Verify;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class DbNameModuleContextOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(DbNameModuleContextOracleTests);

    protected override SqlServerOptions Options => SqlServerOptions.LocalDockerLatest;

    protected override string Ddl => "CREATE TABLE dbo.Marker (Id INT NOT NULL);\nGO\nCREATE PROCEDURE dbo.usp_WhereAmI AS SELECT DB_NAME();";

    [Fact]
    public async Task DbNameInsideAStoredProcedure_ReturnsTheDatabaseTheProcedureLivesInEvenWhenCalledFromAnotherDatabase()
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString("master"));
        await connection.OpenAsync();
        await using var command = new SqlCommand($"EXEC [{DatabaseName}].dbo.usp_WhereAmI;", connection);

        var reported = (string?)await command.ExecuteScalarAsync();

        Assert.Equal(DatabaseName, reported);
    }
}
