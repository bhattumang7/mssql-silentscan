using System.Text.Json;
using SilentScan.Core.Reporting;
using SilentScan.Core.Reporting.Sarif;

namespace SilentScan.Live.Sweep;

public static class ScanReportRuleIds
{
    public static IReadOnlySet<string> Extract(ScanReport report)
    {
        var sarifJson = SarifReportWriter.Write(report);
        using var document = JsonDocument.Parse(sarifJson);

        var ruleIds = new HashSet<string>(StringComparer.Ordinal);
        var results = document.RootElement.GetProperty("runs")[0].GetProperty("results");
        foreach (var result in results.EnumerateArray())
        {
            ruleIds.Add(result.GetProperty("ruleId").GetString()!);
        }

        return ruleIds;
    }
}
