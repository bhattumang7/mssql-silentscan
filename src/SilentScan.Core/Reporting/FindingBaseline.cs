using System.Globalization;
using System.Text.Json;
using SilentScan.Core.Predicates;

namespace SilentScan.Core.Reporting;

public sealed record BaselineApplication(ScanReport Report, int Suppressed);

public sealed class FindingBaseline
{
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly IReadOnlyDictionary<string, int> counts;

    private FindingBaseline(IReadOnlyDictionary<string, int> counts)
    {
        this.counts = counts;
    }

    public static FindingBaseline Capture(ScanReport report)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (scannerKey, findings) in report.FindingsByRuleId)
        {
            foreach (var finding in findings)
            {
                var key = KeyOf(scannerKey, finding);
                counts[key] = counts.GetValueOrDefault(key) + 1;
            }
        }

        return new FindingBaseline(counts);
    }

    public BaselineApplication Apply(ScanReport report)
    {
        var remainingAllowance = new Dictionary<string, int>(counts, StringComparer.Ordinal);
        var suppressed = 0;
        var updated = new Dictionary<string, IReadOnlyList<IFinding>>(StringComparer.Ordinal);

        foreach (var (scannerKey, findings) in report.FindingsByRuleId)
        {
            var kept = new List<IFinding>();
            foreach (var finding in findings.OrderBy(f => f.Location.SourcePath, StringComparer.Ordinal).ThenBy(f => f.Location.Line).ThenBy(f => f.Location.Column))
            {
                var key = KeyOf(scannerKey, finding);
                if (remainingAllowance.TryGetValue(key, out var allowance) && allowance > 0)
                {
                    remainingAllowance[key] = allowance - 1;
                    suppressed++;
                    continue;
                }

                kept.Add(finding);
            }

            updated[scannerKey] = kept;
        }

        return new BaselineApplication(report with { FindingsByRuleId = updated }, suppressed);
    }

    public string ToJson()
    {
        var entries = counts
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new BaselineEntry(pair.Key, pair.Value))
            .ToList();
        return JsonSerializer.Serialize(new BaselineDocument(CurrentVersion, entries), JsonOptions);
    }

    public static FindingBaseline FromJson(string json)
    {
        BaselineDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<BaselineDocument>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The baseline file is not valid JSON.", ex);
        }

        if (document is null || document.Entries is null)
        {
            throw new InvalidDataException("The baseline file has no entries.");
        }

        if (document.Version != CurrentVersion)
        {
            throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture, $"The baseline file has unsupported version {document.Version}; expected {CurrentVersion}."));
        }

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var entry in document.Entries)
        {
            counts[entry.Key] = counts.GetValueOrDefault(entry.Key) + entry.Count;
        }

        return new FindingBaseline(counts);
    }

    private static string KeyOf(string scannerKey, IFinding finding)
    {
        var type = finding.GetType();
        var kind = type.GetProperty("Kind")?.GetValue(finding)?.ToString() ?? string.Empty;
        return $"{scannerKey}|{type.Name}|{kind}|{finding.Location.SourcePath}";
    }

    private sealed record BaselineEntry(string Key, int Count);

    private sealed record BaselineDocument(int Version, List<BaselineEntry>? Entries);
}
