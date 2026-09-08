using System.Text.Json;
using SilentScan.Live.Sweep;
using SilentScan.Verify;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Sweep")]
public sealed class RuleCorpusSweepTests
{
    private sealed record BaselineEntry(string RuleId, string Variant, int ExampleIndex, string Outcome, string Reason);

    private static readonly JsonSerializerOptions BaselineJsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Sweep_MatchesBaseline_NoNewFailuresAndNoStaleEntries()
    {
        var baselinePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "sweep", "baseline.json");
        var baseline = JsonSerializer.Deserialize<List<BaselineEntry>>(
            await File.ReadAllTextAsync(baselinePath),
            BaselineJsonOptions)!;
        var baselineByKey = baseline.ToDictionary(e => (e.RuleId, e.Variant, e.ExampleIndex));

        var cases = RuleExampleCorpus.Build();
        var results = await SweepRunner.RunAsync(cases, SqlServerOptions.LocalDocker);

        var newFailures = new List<string>();
        var staleEntries = new List<string>();

        foreach (var result in results)
        {
            var key = (result.Case.RuleId, result.Case.Variant.ToString(), result.Case.ExampleIndex);
            var isBaselined = baselineByKey.TryGetValue(key, out var entry);

            if (result.Outcome.ToString() == "Confirmed")
            {
                if (isBaselined)
                {
                    staleEntries.Add($"{key.RuleId} [{key.Item2}] #{key.ExampleIndex}: baseline says \"{entry!.Outcome}\" but the sweep now confirms it - remove this baseline entry");
                }

                continue;
            }

            if (!isBaselined || entry!.Outcome != result.Outcome.ToString())
            {
                newFailures.Add($"{key.RuleId} [{key.Item2}] #{key.ExampleIndex}: {result.Outcome} - {result.Detail ?? "(no detail)"}");
            }
        }

        Assert.True(
            newFailures.Count == 0 && staleEntries.Count == 0,
            "New sweep failures:\n" + string.Join('\n', newFailures)
            + "\n\nStale baseline entries (now passing, remove them):\n" + string.Join('\n', staleEntries));
    }
}
