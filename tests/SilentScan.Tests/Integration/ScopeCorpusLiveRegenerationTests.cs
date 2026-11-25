using SilentScan.Tests.Support;
using SilentScan.Verify;
using SilentScan.Verify.Oracle;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class ScopeCorpusLiveRegenerationTests
{
    [Fact]
    public Task LiveScenarios_MatchCheckedInCorpus() => AssertMatchesAsync(SqlServerOptions.LocalDocker);

    [Fact]
    public Task LatestEngineScenarios_MatchCheckedInCorpus() => AssertMatchesAsync(SqlServerOptions.LocalDockerLatest);

    private static async Task AssertMatchesAsync(SqlServerOptions options)
    {
        var runner = new ScopeScenarioRunner(options);
        var (live, _) = await runner.RunAsync(ScopeScenarios.All);
        var pinned = ScopeCorpus.Load().ToDictionary(e => e.Id, e => e.Observed);
        var mismatches = new List<string>();

        foreach (var observation in live)
        {
            if (!pinned.TryGetValue(observation.Id, out var expected))
            {
                mismatches.Add($"{observation.Id}: observed live but MISSING from the checked-in corpus.");
            }
            else if (expected != observation.Observed)
            {
                mismatches.Add($"{observation.Id}: checked-in '{expected}' vs live '{observation.Observed}'.");
            }
        }

        Assert.True(mismatches.Count == 0, "ScopeScenarioCorpus.json has drifted from the live oracle:\n" + string.Join("\n", mismatches));
        Assert.Equal(pinned.Count, live.Count);
    }

    [Fact]
    public async Task RawEngine_DeclareInLoopKeepsItsValue_WhileTheControlResets()
    {
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(SqlServerOptions.LocalDocker.BuildConnectionString("tempdb"));
        await connection.OpenAsync();

        var kept = await ScalarAsync(connection, "DECLARE @i INT = 0, @r VARCHAR(20) = ''; WHILE @i < 2 BEGIN DECLARE @y INT; SET @y = ISNULL(@y, 0) + 1; SET @r += CAST(@y AS VARCHAR(3)); SET @i += 1; END SELECT @r;");
        var reset = await ScalarAsync(connection, "DECLARE @i INT = 0, @r VARCHAR(20) = ''; WHILE @i < 2 BEGIN DECLARE @y INT = NULL; SET @y = ISNULL(@y, 0) + 1; SET @r += CAST(@y AS VARCHAR(3)); SET @i += 1; END SELECT @r;");

        Assert.Equal("12", kept);
        Assert.Equal("11", reset);
        Assert.Equal("12", ScopeCorpus.Get("declare-in-loop-keeps-value-across-iterations").Observed);
    }

    private static async Task<string> ScalarAsync(Microsoft.Data.SqlClient.SqlConnection connection, string sql)
    {
        await using var command = new Microsoft.Data.SqlClient.SqlCommand(sql, connection);
        return (string)(await command.ExecuteScalarAsync())!;
    }
}
