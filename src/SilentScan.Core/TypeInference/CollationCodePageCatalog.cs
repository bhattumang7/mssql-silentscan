using System.Text.Json;
using System.Text.Json.Serialization;

namespace SilentScan.Core.TypeInference;

public static class CollationCodePageCatalog
{
    private static readonly Dictionary<string, int> CodePagesByCollation = LoadEmbedded();

    public static int? TryGetCodePage(string? collationName)
    {
        if (collationName is null)
        {
            return null;
        }

        return CodePagesByCollation.TryGetValue(collationName, out var codePage) ? codePage : null;
    }

    private static Dictionary<string, int> LoadEmbedded()
    {
        var assembly = typeof(CollationCodePageCatalog).Assembly;
        var resourceName = $"{assembly.GetName().Name}.TypeInference.CollationCodePages.json";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found - the collation code page data is missing from the build.");

        var document = JsonSerializer.Deserialize<CodePageDocument>(stream, JsonOptions)
            ?? throw new InvalidOperationException("CollationCodePages.json deserialized to null.");

        return new Dictionary<string, int>(document.CodePages, StringComparer.OrdinalIgnoreCase);
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed record CodePageDocument(
        [property: JsonPropertyName("ServerVersion")] string ServerVersion,
        [property: JsonPropertyName("ProbedAtUtc")] string ProbedAtUtc,
        [property: JsonPropertyName("Notes")] string Notes,
        [property: JsonPropertyName("CodePages")] IReadOnlyDictionary<string, int> CodePages);
}
