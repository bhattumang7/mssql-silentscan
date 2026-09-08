using System.Globalization;
using System.Text;
using System.Text.Json;

namespace SilentScan.Live.Sweep;

public static class SweepReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string WriteJson(IReadOnlyList<SweepResult> results) => JsonSerializer.Serialize(
        results.Select(r => new
        {
            r.Case.RuleId,
            r.Case.ExampleIndex,
            r.Case.Title,
            Variant = r.Case.Variant.ToString(),
            Outcome = r.Outcome.ToString(),
            r.Detail,
            r.ForeignRuleIdsFired,
        }),
        JsonOptions);

    public static string WriteReadable(IReadOnlyList<SweepResult> results)
    {
        var sb = new StringBuilder();
        var byOutcome = results.GroupBy(r => r.Outcome).OrderBy(g => g.Key.ToString(), StringComparer.Ordinal);

        sb.AppendLine(CultureInfo.InvariantCulture, $"{results.Count} case(s) swept.");
        foreach (var group in byOutcome)
        {
            sb.AppendLine();
            sb.AppendLine(CultureInfo.InvariantCulture, $"== {group.Key} ({group.Count()}) ==");
            foreach (var result in group.OrderBy(r => r.Case.RuleId, StringComparer.Ordinal))
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"  {result.Case.RuleId} [{result.Case.Variant}] example #{result.Case.ExampleIndex} \"{result.Case.Title}\"");
                if (result.Detail is not null)
                {
                    sb.AppendLine(CultureInfo.InvariantCulture, $"    {result.Detail}");
                }
            }
        }

        var foreignFireCounts = results
            .SelectMany(r => r.ForeignRuleIdsFired.Select(ruleId => (Case: r.Case, RuleId: ruleId)))
            .GroupBy(x => x.RuleId)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal);

        sb.AppendLine();
        sb.AppendLine("== cross-rule fire matrix (rule fired on another rule's example) ==");
        foreach (var group in foreignFireCounts)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"  {group.Key}: fired on {group.Count()} foreign example(s)");
        }

        return sb.ToString();
    }

    public static string WriteJson(IReadOnlyList<MetamorphicResult> results) => JsonSerializer.Serialize(
        results.Select(r => new
        {
            r.BaseCase.RuleId,
            r.BaseCase.ExampleIndex,
            r.MutationName,
            Outcome = r.Outcome.ToString(),
            r.BaselineFiredRuleIds,
            r.MutatedFiredRuleIds,
        }),
        JsonOptions);

    public static string WriteReadable(IReadOnlyList<MetamorphicResult> results)
    {
        var sb = new StringBuilder();
        var mismatches = results.Where(r => r.Outcome == MetamorphicOutcome.Mismatched).ToList();

        sb.AppendLine(CultureInfo.InvariantCulture, $"{results.Count} mutation(s) parsed and deployed; {mismatches.Count} changed the fired-rule-id set.");
        foreach (var mismatch in mismatches.OrderBy(r => r.BaseCase.RuleId, StringComparer.Ordinal).ThenBy(r => r.MutationName, StringComparer.Ordinal))
        {
            sb.AppendLine();
            sb.AppendLine(CultureInfo.InvariantCulture, $"  {mismatch.BaseCase.RuleId} example #{mismatch.BaseCase.ExampleIndex} x {mismatch.MutationName}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"    baseline: {string.Join(", ", mismatch.BaselineFiredRuleIds.OrderBy(id => id, StringComparer.Ordinal))}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"    mutated:  {string.Join(", ", mismatch.MutatedFiredRuleIds.OrderBy(id => id, StringComparer.Ordinal))}");
        }

        return sb.ToString();
    }
}
