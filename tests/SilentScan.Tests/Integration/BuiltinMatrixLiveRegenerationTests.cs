using SilentScan.Core.Rules;
using SilentScan.Verify;
using SilentScan.Verify.Oracle;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class BuiltinMatrixLiveRegenerationTests
{
    [Fact]
    public Task LiveMatrix_MatchesCheckedInMatrix() => AssertMatchesAsync(SqlServerOptions.LocalDocker);

    [Fact]
    public Task LatestEngineMatrix_MatchesCheckedInMatrix() => AssertMatchesAsync(SqlServerOptions.LocalDockerLatest);

    [Fact]
    public Task LatestEngineOnlyFunctions_MatchCheckedInLatestMatrix() =>
        AssertMatchesAsync(SqlServerOptions.LocalDockerLatest, BuiltinProbes.LatestOnly, BuiltinMatrix.Latest);

    private static Task AssertMatchesAsync(SqlServerOptions options) => AssertMatchesAsync(options, BuiltinProbes.All, BuiltinMatrix.Instance);

    private static async Task AssertMatchesAsync(SqlServerOptions options, IReadOnlyList<BuiltinProbe> probes, BuiltinMatrix checkedIn)
    {
        var generator = new BuiltinMatrixGenerator(options);
        var (live, _) = await generator.GenerateAsync(probes);
        var mismatches = new List<string>();

        foreach (var cell in live)
        {
            var pinned = checkedIn.TryGet(cell.Function, cell.Shape);
            if (pinned != cell)
            {
                mismatches.Add($"{cell.Function} [{cell.Shape}]: checked-in {pinned} vs live {cell}.");
            }
        }

        Assert.True(mismatches.Count == 0, "BuiltinMatrix.json has drifted from the live oracle:\n" + string.Join("\n", mismatches.Take(40)));
        Assert.Equal(checkedIn.Entries.Count, live.Count);
    }

    [Fact]
    public async Task RawEngine_AgreesWithMatrix_ForLenOfMaxAndReplicateCap()
    {
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(SqlServerOptions.LocalDocker.BuildConnectionString("tempdb"));
        await connection.OpenAsync();
        await using var command = new Microsoft.Data.SqlClient.SqlCommand(
            "DECLARE @m VARCHAR(MAX) = 'a', @v VARCHAR(20) = 'a'; SELECT CAST(SQL_VARIANT_PROPERTY(LEN(@m), 'BaseType') AS VARCHAR(20)) + '|' + CAST(SQL_VARIANT_PROPERTY(LEN(@v), 'BaseType') AS VARCHAR(20)) + '|' + CAST(LEN(REPLICATE(@v, 9000)) AS VARCHAR(10));", connection);

        Assert.Equal("bigint|int|8000", (string)(await command.ExecuteScalarAsync())!);
        Assert.Equal("varchar(8000)", BuiltinMatrix.Instance.TryGet("REPLICATE", "VARCHAR(20);INT")!.ResultType);
    }
}
