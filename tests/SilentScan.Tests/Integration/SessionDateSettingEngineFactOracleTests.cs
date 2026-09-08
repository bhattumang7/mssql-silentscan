using Microsoft.Data.SqlClient;
using SilentScan.Verify;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class SessionDateSettingEngineFactOracleTests
{
    private static readonly SqlServerOptions Options = SqlServerOptions.LocalDocker;

    [Fact]
    public async Task SetDateFormat_ChangesHowAnAmbiguousDateLiteralParsesForTheRestOfTheSession()
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString());
        await connection.OpenAsync();

        await using var beforeCommand = connection.CreateCommand();
        beforeCommand.CommandText = "SELECT CONVERT(DATE, '02/01/2024');";
        var beforeParse = (DateTime)(await beforeCommand.ExecuteScalarAsync())!;
        Assert.Equal(new DateTime(2024, 2, 1), beforeParse);

        await using var setCommand = connection.CreateCommand();
        setCommand.CommandText = "SET DATEFORMAT dmy;";
        await setCommand.ExecuteNonQueryAsync();

        await using var afterCommand = connection.CreateCommand();
        afterCommand.CommandText = "SELECT CONVERT(DATE, '02/01/2024');";
        var afterParse = (DateTime)(await afterCommand.ExecuteScalarAsync())!;
        Assert.Equal(new DateTime(2024, 1, 2), afterParse);
    }

    [Fact]
    public async Task SetDateFirst_ChangesAtAtDateFirstForTheRestOfTheSession()
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString());
        await connection.OpenAsync();

        await using var beforeCommand = connection.CreateCommand();
        beforeCommand.CommandText = "SELECT @@DATEFIRST;";
        var beforeDateFirst = (byte)(await beforeCommand.ExecuteScalarAsync())!;
        Assert.Equal(7, beforeDateFirst);

        await using var setCommand = connection.CreateCommand();
        setCommand.CommandText = "SET DATEFIRST 1;";
        await setCommand.ExecuteNonQueryAsync();

        await using var afterCommand = connection.CreateCommand();
        afterCommand.CommandText = "SELECT @@DATEFIRST;";
        var afterDateFirst = (byte)(await afterCommand.ExecuteScalarAsync())!;
        Assert.Equal(1, afterDateFirst);
    }
}
