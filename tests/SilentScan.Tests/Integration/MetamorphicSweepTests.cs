using System.Text.Json;
using SilentScan.Live.Sweep;
using SilentScan.Verify;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Sweep")]
public sealed class MetamorphicSweepTests
{
    private sealed record BaselineEntry(string RuleId, int ExampleIndex, string MutationName, string Reason);

    private static readonly JsonSerializerOptions BaselineJsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task RunAsync_FullCorpus_MatchesBaseline_NoNewMismatchesAndNoStaleEntries()
    {
        var baselinePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "sweep", "metamorphic-baseline.json");
        var baseline = JsonSerializer.Deserialize<List<BaselineEntry>>(
            await File.ReadAllTextAsync(baselinePath),
            BaselineJsonOptions)!;
        var baselineKeys = baseline.Select(e => (e.RuleId, e.ExampleIndex, e.MutationName)).ToHashSet();

        var cases = RuleExampleCorpus.Build();
        var results = await MetamorphicSweepRunner.RunAsync(cases, SqlServerOptions.LocalDocker);

        var newMismatches = new List<string>();
        var staleEntries = new List<string>();

        var resultKeys = new HashSet<(string RuleId, int ExampleIndex, string MutationName)>();
        foreach (var result in results)
        {
            var key = (result.BaseCase.RuleId, result.BaseCase.ExampleIndex, result.MutationName);
            resultKeys.Add(key);
            var isBaselined = baselineKeys.Contains(key);

            if (result.Outcome == MetamorphicOutcome.Matched)
            {
                if (isBaselined)
                {
                    staleEntries.Add($"{key.RuleId} #{key.ExampleIndex} x {key.MutationName}: baselined as a mismatch but the sweep now matches - remove this baseline entry");
                }

                continue;
            }

            if (!isBaselined)
            {
                newMismatches.Add(
                    $"{key.RuleId} #{key.ExampleIndex} x {key.MutationName}: baseline=[{string.Join(",", result.BaselineFiredRuleIds)}] mutated=[{string.Join(",", result.MutatedFiredRuleIds)}]");
            }
        }

        foreach (var entry in baseline.Where(e => !resultKeys.Contains((e.RuleId, e.ExampleIndex, e.MutationName))))
        {
            staleEntries.Add($"{entry.RuleId} #{entry.ExampleIndex} x {entry.MutationName}: baselined but no longer produced by the sweep - remove this baseline entry");
        }

        Assert.True(
            newMismatches.Count == 0 && staleEntries.Count == 0,
            "New metamorphic mismatches:\n" + string.Join('\n', newMismatches)
            + "\n\nStale baseline entries:\n" + string.Join('\n', staleEntries));
    }
}
