using SilentScan.Core.Rules;
using SilentScan.Verify;
using SilentScan.Verify.Oracle;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class SargShapeMatrixLiveRegenerationTests
{
    [Fact]
    public Task LiveMatrix_MatchesCheckedInMatrix() => AssertMatchesAsync(SqlServerOptions.LocalDocker);

    [Fact]
    public Task LatestEngineMatrix_MatchesCheckedInMatrix() => AssertMatchesAsync(SqlServerOptions.LocalDockerLatest);

    private static async Task AssertMatchesAsync(SqlServerOptions options)
    {
        var generator = new SargShapeMatrixGenerator(options);
        var (live, _) = await generator.GenerateAsync(SargShapes.ColumnTypes, SargShapes.All);
        var checkedIn = SargShapeMatrix.Instance;
        var mismatches = new List<string>();

        foreach (var cell in live)
        {
            var pinned = checkedIn.TryGetOutcome(cell.Shape, cell.ColumnType);
            if (pinned != cell.Outcome)
            {
                mismatches.Add($"{cell.Shape} over {cell.ColumnType}: checked-in '{pinned}' vs live '{cell.Outcome}'.");
            }
        }

        Assert.True(mismatches.Count == 0, "SargShapeMatrix.json has drifted from the live oracle:\n" + string.Join("\n", mismatches.Take(40)));
        Assert.Equal(checkedIn.Entries.Count, live.Count);
    }

    [Fact]
    public async Task RawEngine_SeeksOnAPrefixLike_AndScansOnAWrappedColumn()
    {
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(SqlServerOptions.LocalDocker.BuildConnectionString("tempdb"));
        await connection.OpenAsync();
        await Exec(connection, "DROP TABLE IF EXISTS sgraw; CREATE TABLE sgraw (c VARCHAR(10), filler CHAR(200)); CREATE INDEX ix_sgraw ON sgraw (c); UPDATE STATISTICS sgraw WITH ROWCOUNT = 1000000, PAGECOUNT = 100000;");
        try
        {
            await Exec(connection, "SET SHOWPLAN_XML ON;");
            var seek = await Plan(connection, "SELECT c FROM sgraw WHERE c LIKE 'ab%'");
            var scan = await Plan(connection, "SELECT c FROM sgraw WHERE LEFT(c, 2) = 'ab'");
            await Exec(connection, "SET SHOWPLAN_XML OFF;");

            Assert.Equal("Seek", SargShapeMatrixGenerator.Classify(seek));
            Assert.Equal("Scan", SargShapeMatrixGenerator.Classify(scan));
            Assert.True(SargShapeMatrix.Instance.PreservesSeek("like-prefix", "VARCHAR(10)"));
            Assert.False(SargShapeMatrix.Instance.PreservesSeek("left", "VARCHAR(10)"));
        }
        finally
        {
            await Exec(connection, "SET SHOWPLAN_XML OFF; DROP TABLE IF EXISTS sgraw;");
        }
    }

    private static async Task Exec(Microsoft.Data.SqlClient.SqlConnection connection, string sql)
    {
        await using var command = new Microsoft.Data.SqlClient.SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string> Plan(Microsoft.Data.SqlClient.SqlConnection connection, string sql)
    {
        await using var command = new Microsoft.Data.SqlClient.SqlCommand(sql, connection);
        return (string)(await command.ExecuteScalarAsync())!;
    }
}
