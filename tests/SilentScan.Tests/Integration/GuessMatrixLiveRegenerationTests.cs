using SilentScan.Core.Rules;
using SilentScan.Verify;
using SilentScan.Verify.Oracle;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class GuessMatrixLiveRegenerationTests
{
    [Fact]
    public Task LiveMatrix_MatchesCheckedInMatrix() => AssertMatchesAsync(SqlServerOptions.LocalDocker);

    [Fact]
    public Task LatestEngineMatrix_MatchesCheckedInMatrix() => AssertMatchesAsync(SqlServerOptions.LocalDockerLatest);

    private static async Task AssertMatchesAsync(SqlServerOptions options)
    {
        var generator = new GuessMatrixGenerator(options);
        var (live, _) = await generator.GenerateAsync();
        var checkedIn = GuessMatrix.Instance.Entries.ToDictionary(e => (e.Shape, e.ColumnType, e.Operand, e.Estimator), e => e.Ratio);
        var mismatches = new List<string>();

        foreach (var cell in live)
        {
            if (!checkedIn.TryGetValue((cell.Shape, cell.ColumnType, cell.Operand, cell.Estimator), out var pinned) || pinned != cell.Ratio)
            {
                mismatches.Add($"{cell.Shape} {cell.ColumnType} {cell.Operand} {cell.Estimator}: checked-in '{pinned}' vs live '{cell.Ratio}'.");
            }
        }

        Assert.True(mismatches.Count == 0, "GuessMatrix.json has drifted from the live oracle:\n" + string.Join("\n", mismatches.Take(40)));
        Assert.Equal(checkedIn.Count, live.Count);
    }

    [Fact]
    public async Task RawEngine_EstimatesAThirdOfTheRowsForAnInequalityWithoutStatistics()
    {
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(SqlServerOptions.LocalDocker.BuildConnectionString("tempdb"));
        await connection.OpenAsync();
        await Exec(connection, "DROP TABLE IF EXISTS gsraw; CREATE TABLE gsraw (c INT, filler CHAR(200)); UPDATE STATISTICS gsraw WITH ROWCOUNT = 1000000, PAGECOUNT = 100000;");
        try
        {
            await Exec(connection, "SET SHOWPLAN_XML ON;");
            await using var command = new Microsoft.Data.SqlClient.SqlCommand("SELECT filler FROM gsraw WHERE c < 5 OPTION (USE HINT('FORCE_DEFAULT_CARDINALITY_ESTIMATION'))", connection);
            var plan = (string)(await command.ExecuteScalarAsync())!;
            await Exec(connection, "SET SHOWPLAN_XML OFF;");

            Assert.Equal("0.3", GuessMatrixGenerator.ReadRatio(plan));
            Assert.Equal(0.3, GuessMatrix.Instance.TryGetRatio("lt", "INT", "Literal", "Default"));
        }
        finally
        {
            await Exec(connection, "SET SHOWPLAN_XML OFF; DROP TABLE IF EXISTS gsraw;");
        }
    }

    private static async Task Exec(Microsoft.Data.SqlClient.SqlConnection connection, string sql)
    {
        await using var command = new Microsoft.Data.SqlClient.SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
