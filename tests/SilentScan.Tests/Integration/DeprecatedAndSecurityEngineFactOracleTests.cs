using SilentScan.Tests.Support;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class DeprecatedAndSecurityEngineFactOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(DeprecatedAndSecurityEngineFactOracleTests);

    protected override string Ddl => """
        CREATE TABLE dbo.Src (Id INT, V INT);
        INSERT INTO dbo.Src VALUES (1, 1);
        GO
        CREATE PROCEDURE dbo.NP;1 AS SELECT 'one' AS V;
        GO
        CREATE PROCEDURE dbo.NP;2 AS SELECT 'two' AS V;
        GO
        CREATE PROCEDURE dbo.PlainProc AS SELECT 'plain' AS V;
        """;

    private const string DeprecatedCounter = "SELECT ISNULL(SUM(cntr_value), 0) FROM sys.dm_os_performance_counters WHERE object_name LIKE '%Deprecated Features%' AND instance_name = '{0}';";

    private static async Task<long> CounterAsync(System.Data.Common.DbConnection connection, string instance)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = DeprecatedCounter.Replace("{0}", instance, StringComparison.Ordinal);
        return Convert.ToInt64(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private async Task<(long ControlDelta, long FeatureDelta)> CounterDeltasAsync(string instance, string controlSql, string featureSql)
    {
        await using var connection = await OpenConnectionAsync();
        var before = await CounterAsync(connection, instance);
        await ExecuteAsync(connection, controlSql);
        var afterControl = await CounterAsync(connection, instance);
        await ExecuteAsync(connection, featureSql);
        var afterFeature = await CounterAsync(connection, instance);
        return (afterControl - before, afterFeature - afterControl);
    }

    [Fact]
    [Trait("Rule", "silentscan/deprecated-syntax/numbered-procedure-definition")]
    [Trait("Rule", "silentscan/deprecated-syntax/numbered-procedure-execution")]
    public async Task NumberedProcedures_AreRegisteredAndExecutableByNumber_PlainProcedureIsNotNumbered()
    {
        var numbers = await RowsAsync("SELECT procedure_number FROM sys.numbered_procedures WHERE object_id = OBJECT_ID('dbo.NP') ORDER BY procedure_number;");

        Assert.Equal([2], numbers.Select(r => Convert.ToInt32(r[0], System.Globalization.CultureInfo.InvariantCulture)).ToArray());
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM sys.numbered_procedures WHERE object_id = OBJECT_ID('dbo.PlainProc');"));
        Assert.Equal("two", await ScalarAsync<string>("EXEC dbo.NP;2;"));
        Assert.Equal("one", await ScalarAsync<string>("EXEC dbo.NP;1;"));
    }

    [Fact]
    [Trait("Rule", "silentscan/deprecated-syntax/table-hint-without-with")]
    public async Task TableHintWithoutWith_IncrementsDeprecatedCounter_WithKeywordDoesNot()
    {
        var (control, feature) = await CounterDeltasAsync("Table hint without WITH", $"SELECT Id FROM dbo.Src WITH (NOLOCK) WHERE V = {Random.Shared.Next()};", $"SELECT Id FROM dbo.Src (NOLOCK) WHERE V = {Random.Shared.Next()};");

        Assert.Equal(0, control);
        Assert.True(feature >= 1);
    }
}
