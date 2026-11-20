using System.Globalization;
using System.Text;
using SilentScan.Core.Predicates;
using SilentScan.Verify.Oracle;

namespace SilentScan.Cli.Commands;

internal static class ModuleComparisonSummary
{
    public static string Render(IReadOnlyList<ModuleComparisonRow> rows, int toolOnlyLimit)
    {
        var builder = new StringBuilder();
        builder.AppendLine(CultureInfo.InvariantCulture, $"total: {rows.Count}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"agree: {CountOutcome(rows, ModuleComparisonOutcome.Agree)}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"tool only (not seen in plan): {CountOutcome(rows, ModuleComparisonOutcome.ToolOnly)}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"engine only (not flagged by tool): {CountOutcome(rows, ModuleComparisonOutcome.EngineOnly)}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"not decided: {CountOutcome(rows, ModuleComparisonOutcome.NotDecided)}");

        var undecided = rows.Where(r => r.Outcome == ModuleComparisonOutcome.NotDecided).ToList();
        var reasonCounts = Enum.GetValues<ModuleNotDecidedReason>()
            .Select(reason => (Reason: reason, Count: undecided.Count(r => r.Reason == reason)))
            .Where(entry => entry.Count > 0)
            .ToList();
        if (reasonCounts.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("not decided by reason:");
            foreach (var (reason, count) in reasonCounts)
            {
                builder.AppendLine(CultureInfo.InvariantCulture, $"  {reason}: {count}");
            }
        }

        var engineOnly = rows.Where(r => r.Outcome == ModuleComparisonOutcome.EngineOnly).ToList();
        if (engineOnly.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("engine only by where in the plan the conversion sits:");
            foreach (var group in engineOnly
                .GroupBy(r => r.Context ?? "Unknown")
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key, StringComparer.Ordinal))
            {
                builder.AppendLine(CultureInfo.InvariantCulture, $"  {group.Key}: {group.Count()}");
            }

            var predicateEngineOnly = engineOnly.Where(r => r.Context is "Predicate" or "SeekPredicates" or "SeekPredicateNew" or "SeekPredicate" or "Residual" or "ProbeResidual").ToList();
            if (predicateEngineOnly.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("engine only inside a predicate, by what the tool said about that column in that statement:");
                foreach (var verdictGroup in predicateEngineOnly
                    .GroupBy(r => r.Finding is null ? "no finding" : r.Finding.Verdict.ToString())
                    .OrderByDescending(g => g.Count())
                    .ThenBy(g => g.Key, StringComparer.Ordinal))
                {
                    builder.AppendLine(CultureInfo.InvariantCulture, $"  {verdictGroup.Key}: {verdictGroup.Count()}");
                }

                builder.AppendLine();
            }
        }

        var toolOnly = rows
            .Where(r => r.Outcome == ModuleComparisonOutcome.ToolOnly && r.Finding is not null)
            .ToList();
        if (toolOnly.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("tool only by type pair:");
            foreach (var group in toolOnly
                .GroupBy(r => CorpusVerificationSummary.DescribeTypePair(r.Finding!))
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key, StringComparer.Ordinal))
            {
                builder.AppendLine(CultureInfo.InvariantCulture, $"  {group.Key}: {group.Count()}");
            }

            builder.AppendLine();
            foreach (var row in toolOnly.Take(toolOnlyLimit))
            {
                var finding = row.Finding!;
                builder.AppendLine(CultureInfo.InvariantCulture, $"tool-only {CorpusVerificationSummary.DescribeTypePair(finding)} op '{finding.Operator}' {finding.Verdict}");
            }
        }

        return builder.ToString();
    }

    public static IEnumerable<object> ToJsonRows(IReadOnlyList<ModuleComparisonRow> rows)
    {
        foreach (var row in rows)
        {
            var finding = row.Finding;
            yield return new
            {
                Outcome = row.Outcome.ToString(),
                Reason = row.Reason.ToString(),
                TypePair = finding is null ? null : CorpusVerificationSummary.DescribeTypePair(finding),
                Operator = finding?.Operator,
                Table = finding?.Column.TableQualifiedName ?? row.Table,
                Column = finding?.Column.ColumnName ?? row.Column,
                row.StatementStartLine,
                SourcePath = finding?.SourcePath,
                Line = finding?.Line,
                Verdict = finding?.Verdict.ToString(),
                row.Detail,
                row.Context,
            };
        }
    }

    private static int CountOutcome(IReadOnlyList<ModuleComparisonRow> rows, ModuleComparisonOutcome outcome) =>
        rows.Count(r => r.Outcome == outcome);
}
