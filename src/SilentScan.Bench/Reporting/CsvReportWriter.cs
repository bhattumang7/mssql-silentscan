using System.Globalization;
using System.Text;

namespace SilentScan.Bench.Reporting;

/// <summary>Writes the cost table CSV (CLAUDE.md: "Output a CSV the writeup can chart directly").</summary>
public static class CsvReportWriter
{
    public static string Write(IReadOnlyList<BenchmarkResult> results)
    {
        var builder = new StringBuilder();
        builder.AppendLine("ScenarioName,RowCount,LegacyCardinalityEstimation,Matched,MedianLogicalReads,MedianCpuMs,MedianElapsedMs");

        foreach (var r in results)
        {
            builder.AppendLine(string.Join(',',
                r.ScenarioName,
                r.RowCount.ToString(CultureInfo.InvariantCulture),
                r.LegacyCardinalityEstimation,
                r.Matched,
                r.MedianLogicalReads.ToString(CultureInfo.InvariantCulture),
                r.MedianCpuMs.ToString(CultureInfo.InvariantCulture),
                r.MedianElapsedMs.ToString(CultureInfo.InvariantCulture)));
        }

        return builder.ToString();
    }
}
