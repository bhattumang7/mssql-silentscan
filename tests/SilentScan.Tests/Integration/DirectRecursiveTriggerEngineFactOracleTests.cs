using Microsoft.Data.SqlClient;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class DirectRecursiveTriggerEngineFactOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(DirectRecursiveTriggerEngineFactOracleTests);

    protected override string Ddl => """
        CREATE TABLE dbo.T6 (Id INT NOT NULL, Val INT NOT NULL);
        INSERT INTO dbo.T6 (Id, Val) VALUES (1, 0);
        GO
        CREATE TABLE dbo.T6Log (FiredAt DATETIME2 NOT NULL DEFAULT SYSDATETIME());
        GO
        CREATE TRIGGER dbo.trg_T6_SelfRecurse ON dbo.T6 AFTER UPDATE AS
        BEGIN
            INSERT INTO dbo.T6Log DEFAULT VALUES;
            UPDATE dbo.T6 SET Val = Val + 1 WHERE Id = 1;
        END;
        GO
        """;

    [Fact]
    public async Task WithRecursiveTriggersOff_TheDefault_ASelfWriteInTheTriggerBodyDoesNotReFireIt()
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(DatabaseName));
        await connection.OpenAsync();

        await using var updateCommand = connection.CreateCommand();
        updateCommand.CommandText = "UPDATE dbo.T6 SET Val = 100 WHERE Id = 1;";
        await updateCommand.ExecuteNonQueryAsync();

        await using var fireCountCommand = connection.CreateCommand();
        fireCountCommand.CommandText = "SELECT COUNT(*) FROM dbo.T6Log;";
        var fireCount = (int)(await fireCountCommand.ExecuteScalarAsync())!;
        Assert.Equal(1, fireCount);
    }

    [Fact]
    public async Task WithRecursiveTriggersOn_TheSameSelfWriteReFiresUntilTheNestingLimitAborts()
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(DatabaseName));
        await connection.OpenAsync();

        await using var enableCommand = connection.CreateCommand();
        enableCommand.CommandText = "ALTER DATABASE CURRENT SET RECURSIVE_TRIGGERS ON;";
        await enableCommand.ExecuteNonQueryAsync();

        await using var updateCommand = connection.CreateCommand();
        updateCommand.CommandText = "UPDATE dbo.T6 SET Val = 100 WHERE Id = 1;";

        var exception = await Assert.ThrowsAsync<SqlException>(() => updateCommand.ExecuteNonQueryAsync());
        Assert.Equal(217, exception.Number);
    }
}
