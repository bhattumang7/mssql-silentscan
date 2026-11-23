using Microsoft.Data.SqlClient;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.QueryAntiPatternMultiRowInsertIgnoreDupKeyDropRuleId)]
public sealed class MultiRowInsertIgnoreDupKeyDropEngineFactOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(MultiRowInsertIgnoreDupKeyDropEngineFactOracleTests);

    protected override string Ddl => """
        CREATE TABLE dbo.T3 (Id INT NOT NULL);
        CREATE UNIQUE INDEX UX_T3_Id ON dbo.T3(Id) WITH (IGNORE_DUP_KEY = ON);
        GO
        INSERT INTO dbo.T3 (Id) VALUES (1);
        GO
        CREATE TABLE dbo.T3Disabled (Id INT NOT NULL);
        CREATE UNIQUE INDEX UX_T3Disabled_Id ON dbo.T3Disabled(Id) WITH (IGNORE_DUP_KEY = ON);
        ALTER INDEX UX_T3Disabled_Id ON dbo.T3Disabled DISABLE;
        GO
        """;

    [Fact]
    public async Task MultiRowInsertColliding_WithAnExistingKey_SilentlyDropsOnlyTheCollidingRowWithNoError()
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(DatabaseName));
        await connection.OpenAsync();

        await using var insertCommand = connection.CreateCommand();
        insertCommand.CommandText = "INSERT INTO dbo.T3 (Id) VALUES (1), (2), (3);";
        var reportedRowCount = await insertCommand.ExecuteNonQueryAsync();
        Assert.Equal(2, reportedRowCount);

        await using var countCommand = connection.CreateCommand();
        countCommand.CommandText = "SELECT COUNT(*) FROM dbo.T3;";
        var actualRowCount = (int)(await countCommand.ExecuteScalarAsync())!;
        Assert.Equal(3, actualRowCount);
    }

    [Fact]
    public async Task MultiRowInsertColliding_IntoADisabledIndex_DropsNothing()
    {
        await ExecuteAsync("INSERT INTO dbo.T3Disabled (Id) VALUES (1), (1), (2);");

        Assert.Equal(3, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.T3Disabled;"));
    }
}
