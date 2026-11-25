using SilentScan.Core.Rules;
using SilentScan.Verify;
using SilentScan.Verify.Oracle;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class RescueMatrixLiveRegenerationTests
{
    [Fact]
    public Task LiveMatrix_MatchesCheckedInMatrix() => AssertMatchesAsync(SqlServerOptions.LocalDocker);

    [Fact]
    public Task LatestEngineMatrix_MatchesCheckedInMatrix() => AssertMatchesAsync(SqlServerOptions.LocalDockerLatest);

    private static async Task AssertMatchesAsync(SqlServerOptions options)
    {
        var (live, _) = await new RescueMatrixGenerator(options).GenerateAsync(RescueScenarios.All);
        var mismatches = live.Where(c => RescueMatrix.Instance.TryGet(c.Id) != c).Select(c => $"{c.Id}: checked-in {RescueMatrix.Instance.TryGet(c.Id)} vs live {c}").ToList();

        Assert.True(mismatches.Count == 0, "RescueMatrix.json has drifted from the live oracle:\n" + string.Join("\n", mismatches));
        Assert.Equal(RescueMatrix.Instance.Entries.Count, live.Count);
    }
}
