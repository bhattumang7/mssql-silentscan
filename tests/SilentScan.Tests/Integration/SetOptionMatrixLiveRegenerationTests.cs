using SilentScan.Core.Rules;
using SilentScan.Verify;
using SilentScan.Verify.Oracle;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class SetOptionMatrixLiveRegenerationTests
{
    [Fact]
    public Task LiveMatrix_MatchesCheckedInMatrix() => AssertMatchesAsync(SqlServerOptions.LocalDocker);

    [Fact]
    public Task LatestEngineMatrix_MatchesCheckedInMatrix() => AssertMatchesAsync(SqlServerOptions.LocalDockerLatest);

    private static async Task AssertMatchesAsync(SqlServerOptions options)
    {
        var generator = new SetOptionMatrixGenerator(options);
        var (live, _) = await generator.GenerateAsync();
        var mismatches = new List<string>();

        foreach (var cell in live)
        {
            var pinned = SetOptionMatrix.Instance.TryGet(cell.Id);
            if (pinned != cell)
            {
                mismatches.Add($"{cell.Id}: checked-in {pinned} vs live {cell}.");
            }
        }

        Assert.True(mismatches.Count == 0, "SetOptionMatrix.json has drifted from the live oracle:\n" + string.Join("\n", mismatches));
        Assert.Equal(SetOptionMatrix.Instance.Entries.Count, live.Count);
    }

    [Fact]
    public async Task RawEngine_EqualsNullMatchesOnlyWhenAnsiNullsIsOff()
    {
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(SqlServerOptions.LocalDocker.BuildConnectionString("tempdb"));
        await connection.OpenAsync();
        const string Probe = "DECLARE @n INT = NULL; SELECT CASE WHEN @n = NULL THEN 'match' ELSE 'nomatch' END";

        Assert.Equal("nomatch", await Scalar(connection, "SET ANSI_NULLS ON; " + Probe));
        Assert.Equal("match", await Scalar(connection, "SET ANSI_NULLS OFF; " + Probe));
        Assert.Equal("match", SetOptionMatrix.Instance.TryGet("equals-null")!.WhenOff);
    }

    private static async Task<string> Scalar(Microsoft.Data.SqlClient.SqlConnection connection, string sql)
    {
        await using var command = new Microsoft.Data.SqlClient.SqlCommand(sql, connection);
        return (string)(await command.ExecuteScalarAsync())!;
    }
}
