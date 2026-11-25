using System.Text.Json;
using System.Text.Json.Serialization;

namespace SilentScan.Tests.Support;

public sealed record ScopeCorpusEntry(
    string Id,
    string Family,
    string Step,
    string Kind,
    bool CaseSensitiveDatabase,
    string Setup,
    string Body,
    string Observed);

public static class ScopeCorpus
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static string CheckedInPath { get; } = Path.Combine(AppContext.BaseDirectory, "fixtures", "scope", "ScopeScenarioCorpus.json");

    public static IReadOnlyList<ScopeCorpusEntry> Load()
    {
        using var stream = File.OpenRead(CheckedInPath);
        var document = JsonSerializer.Deserialize<CorpusDocument>(stream, JsonOptions)
            ?? throw new InvalidOperationException("ScopeScenarioCorpus.json deserialized to null.");
        return document.Entries;
    }

    public static ScopeCorpusEntry Get(string id) => Load().Single(e => e.Id == id);

    private sealed record CorpusDocument([property: JsonPropertyName("Entries")] IReadOnlyList<ScopeCorpusEntry> Entries);
}
