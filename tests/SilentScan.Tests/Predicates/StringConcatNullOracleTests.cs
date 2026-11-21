using Microsoft.Data.SqlClient;
using SilentScan.Verify;
using SilentScan.Verify.Deployment;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.StringConcatNullRuleId)]
[Trait("Rule", FindingRuleIds.SetOptionConcatNullYieldsNullOffBlocksIndexedFeatureRuleId)]
public sealed class StringConcatNullOracleTests : IAsyncLifetime
{
    private static readonly SqlServerOptions Options = SqlServerOptions.LocalDockerLatest;

    private readonly string _databaseName = $"{nameof(StringConcatNullOracleTests)}_{Guid.NewGuid():N}";

    public async Task InitializeAsync()
    {
        await new DatabaseProvisioner(Options).CreateFreshAsync(_databaseName);
        await new ScriptDeployer(Options).DeployAsync("ALTER DATABASE CURRENT SET COMPATIBILITY_LEVEL = 170;", _databaseName);
    }

    public async Task DisposeAsync() => await new DatabaseProvisioner(Options).DropIfExistsAsync(_databaseName);

    [Fact]
    public async Task CompatLevel170_ConcatNullYieldsNullOff_PlusOperatorTreatsNullAsEmptyString()
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(_databaseName));
        await connection.OpenAsync();

        await using (var setCommand = new SqlCommand("SET CONCAT_NULL_YIELDS_NULL OFF;", connection))
        {
            await setCommand.ExecuteNonQueryAsync();
        }

        await using var probeCommand = new SqlCommand("SELECT 'a' + CAST(NULL AS VARCHAR(1));", connection);
        var result = await probeCommand.ExecuteScalarAsync();

        Assert.Equal("a", result);
    }

    [Fact]
    public async Task CompatLevel170_ConcatNullYieldsNullDefaultOn_PlusOperatorPropagatesNull()
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(_databaseName));
        await connection.OpenAsync();

        await using var probeCommand = new SqlCommand("SELECT 'a' + CAST(NULL AS VARCHAR(1));", connection);
        var result = await probeCommand.ExecuteScalarAsync();

        Assert.Equal(DBNull.Value, result);
    }

    [Fact]
    public async Task CompatLevel170_ConcatNullYieldsNullOff_PersistsAcrossSeparateBatchOnSameSession()
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(_databaseName));
        await connection.OpenAsync();

        await using (var setCommand = new SqlCommand("SET CONCAT_NULL_YIELDS_NULL OFF;", connection))
        {
            await setCommand.ExecuteNonQueryAsync();
        }

        await using (var probeCommand = new SqlCommand("SELECT 'a' + CAST(NULL AS VARCHAR(1));", connection))
        {
            var firstBatchResult = await probeCommand.ExecuteScalarAsync();
            Assert.Equal("a", firstBatchResult);
        }

        await using (var probeCommand = new SqlCommand("SELECT 'b' + CAST(NULL AS VARCHAR(1));", connection))
        {
            var secondBatchResult = await probeCommand.ExecuteScalarAsync();
            Assert.Equal("b", secondBatchResult);
        }
    }

    [Fact]
    public async Task ForXmlPathDelimiterPrefix_NullOperandOmitsTheWholeElementWhilePlainSelectYieldsNull()
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(_databaseName));
        await connection.OpenAsync();

        await using var command = new SqlCommand(
            """
            SELECT
                (SELECT ',' + v FROM (VALUES ('a'), (CAST(NULL AS VARCHAR(5))), ('c')) AS t(v) FOR XML PATH('')),
                (SELECT COUNT(*) FROM (VALUES ('a'), (CAST(NULL AS VARCHAR(5))), ('c')) AS t(v) WHERE ',' + v IS NULL);
            """, connection);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());

        Assert.Equal(",a,c", reader.GetString(0));
        Assert.Equal(1, reader.GetInt32(1));
    }

    [Fact]
    public async Task WhereClauseRejectingNull_LeavesNoNullConcatResult_WhileUnfilteredQueryYieldsOne()
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(_databaseName));
        await connection.OpenAsync();

        await using var command = new SqlCommand(
            """
            SELECT
                (SELECT COUNT(*) FROM (VALUES ('a'), (CAST(NULL AS VARCHAR(5))), ('c')) AS t(v) WHERE v <> '' AND 'x' + v IS NULL),
                (SELECT COUNT(*) FROM (VALUES ('a'), (CAST(NULL AS VARCHAR(5))), ('c')) AS t(v) WHERE 'x' + v IS NULL);
            """, connection);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());

        Assert.Equal(0, reader.GetInt32(0));
        Assert.Equal(1, reader.GetInt32(1));
    }
}
