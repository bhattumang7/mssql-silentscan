using Microsoft.Data.SqlClient;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class NoEarlyOutForEmptyInvocationEngineFactOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(NoEarlyOutForEmptyInvocationEngineFactOracleTests);

    protected override string Ddl => """
        CREATE TABLE dbo.T5 (Id INT NOT NULL, Val INT NOT NULL);
        INSERT INTO dbo.T5 (Id, Val) VALUES (1, 10);
        GO
        CREATE TABLE dbo.T5Log (FiredAt DATETIME2 NOT NULL DEFAULT SYSDATETIME());
        GO
        CREATE TRIGGER dbo.trg_T5_Update ON dbo.T5 AFTER UPDATE AS
        BEGIN
            INSERT INTO dbo.T5Log DEFAULT VALUES;
        END;
        GO
        """;

    [Fact]
    public async Task UpdateMatchingZeroRows_StillFiresTheAfterTrigger()
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(DatabaseName));
        await connection.OpenAsync();

        await using var updateCommand = connection.CreateCommand();
        updateCommand.CommandText = "UPDATE dbo.T5 SET Val = 999 WHERE Id = 99999;";
        await updateCommand.ExecuteNonQueryAsync();

        await using var untouchedCommand = connection.CreateCommand();
        untouchedCommand.CommandText = "SELECT Val FROM dbo.T5 WHERE Id = 1;";
        var untouchedVal = (int)(await untouchedCommand.ExecuteScalarAsync())!;
        Assert.Equal(10, untouchedVal);

        await using var fireCountCommand = connection.CreateCommand();
        fireCountCommand.CommandText = "SELECT COUNT(*) FROM dbo.T5Log;";
        var fireCount = (int)(await fireCountCommand.ExecuteScalarAsync())!;
        Assert.Equal(1, fireCount);
    }
}
