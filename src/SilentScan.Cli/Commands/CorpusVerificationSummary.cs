using System.Globalization;
using System.Text;
using SilentScan.Core.Predicates;
using SilentScan.Core.TypeInference;
using SilentScan.Verify.Oracle;

namespace SilentScan.Cli.Commands;

internal static class CorpusVerificationSummary
{
    public static string Classify(CorpusFindingResult result) =>
        result.Outcome == CorpusFindingOutcome.NotConfirmed
            ? ClassifyNotConfirmed(result)
            : result.Outcome.ToString();

    private static string ClassifyNotConfirmed(CorpusFindingResult result)
    {
        if (result.ColumnConverted != false)
        {
            return "NotConfirmed:shape-mismatch";
        }

        return result.DynamicRangeSeekObserved
            ? "NotConfirmed:dynamic-range-seek-without-column-conversion"
            : "NotConfirmed:no-conversion";
    }

    public static string DescribeTypePair(TypedPredicateFinding finding) =>
        $"{DescribeType(finding.Column.Type)} -> {(finding.OtherOperand is PredicateOperand.Value { Type: var other } ? DescribeType(other) : finding.OtherOperand is PredicateOperand.Column c ? DescribeType(c.Type) : "?")}";

    public static string Render(IReadOnlyList<CorpusFindingResult> results, int missLimit)
    {
        var builder = new StringBuilder();
        builder.AppendLine(CultureInfo.InvariantCulture, $"total: {results.Count}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"conversion confirmed: {results.Count(IsConversionConfirmed)}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"conversion refuted: {results.Count(IsConversionRefuted)}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"not decided: {results.Count(r => !IsConversionConfirmed(r) && !IsConversionRefuted(r))}");

        builder.AppendLine();
        builder.AppendLine("by outcome:");
        foreach (var group in results.GroupBy(Classify).OrderByDescending(g => g.Count()))
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"  {group.Key}: {group.Count()}");
        }

        builder.AppendLine();
        builder.AppendLine("by verdict and outcome:");
        foreach (var group in results.GroupBy(r => (r.Finding.Verdict, Outcome: Classify(r))).OrderBy(g => g.Key.Verdict).ThenByDescending(g => g.Count()))
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"  {group.Key.Verdict} / {group.Key.Outcome}: {group.Count()}");
        }

        builder.AppendLine();
        builder.AppendLine("refuted or shape-mismatched by type pair:");
        var misses = results.Where(r => r.Outcome == CorpusFindingOutcome.NotConfirmed).ToList();
        foreach (var group in misses.GroupBy(r => (Pair: DescribeTypePair(r.Finding), Outcome: Classify(r))).OrderByDescending(g => g.Count()))
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"  {group.Key.Pair} / {group.Key.Outcome}: {group.Count()}");
        }

        builder.AppendLine();
        foreach (var miss in misses.Take(missLimit))
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"miss {miss.Finding.Verdict} {DescribeTypePair(miss.Finding)} op '{miss.Finding.Operator}' {Classify(miss)}");
        }

        return builder.ToString();
    }

    private static bool IsConversionConfirmed(CorpusFindingResult result) =>
        result.Outcome is CorpusFindingOutcome.Confirmed or CorpusFindingOutcome.ConfirmedUnindexed or CorpusFindingOutcome.ConfirmedViaScratchIndex
        || (result.Outcome == CorpusFindingOutcome.NotConfirmed && result.ColumnConverted == true);

    private static bool IsConversionRefuted(CorpusFindingResult result) =>
        result.Outcome == CorpusFindingOutcome.NotConfirmed && result.ColumnConverted == false;

    private static string DescribeType(SqlType? type) =>
        type is null ? "?"
        : type.Collation is { } collation ? $"{type.Category}[{(collation.IsSqlFamily ? "sql" : "windows")}]"
        : type.Category.ToString();
}
