using System.Text.Json;
using System.Text.Json.Serialization;

namespace SilentScan.Core.Rules;

public sealed record RescueCell(string Id, string Family, string Predicate, string Outcome, string OutcomeWithLookup);

public sealed class RescueMatrix
{
    public const string SeekOutcome = "Seek";

    public const string ScanOutcome = "Scan";

    public const string NoTableAccessOutcome = "NoTableAccess";

    public static RescueMatrix Instance { get; } = LoadEmbedded();

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly Dictionary<string, RescueCell> _byId;

    internal RescueMatrix(string serverVersion, string probedAtUtc, IReadOnlyList<RescueCell> entries)
    {
        ServerVersion = serverVersion;
        ProbedAtUtc = probedAtUtc;
        Entries = entries;
        _byId = entries.ToDictionary(e => e.Id);
    }

    public string ServerVersion { get; }

    public string ProbedAtUtc { get; }

    public IReadOnlyList<RescueCell> Entries { get; }

    public RescueCell? TryGet(string id) => _byId.GetValueOrDefault(id);

    public bool PreservesSeekOrAvoidsTable(string id) =>
        TryGet(id) is { } cell && !cell.Outcome.StartsWith(ScanOutcome, StringComparison.Ordinal);

    private static RescueMatrix LoadEmbedded()
    {
        var assembly = typeof(RescueMatrix).Assembly;
        var resourceName = $"{assembly.GetName().Name}.Rules.RescueMatrix.json";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found - the matrix data is missing from the build.");

        var document = JsonSerializer.Deserialize<MatrixDocument>(stream, JsonOptions)
            ?? throw new InvalidOperationException("RescueMatrix.json deserialized to null.");

        return new RescueMatrix(document.ServerVersion, document.ProbedAtUtc, document.Entries);
    }

    private sealed record MatrixDocument(
        [property: JsonPropertyName("ServerVersion")] string ServerVersion,
        [property: JsonPropertyName("ProbedAtUtc")] string ProbedAtUtc,
        [property: JsonPropertyName("Notes")] string Notes,
        [property: JsonPropertyName("Entries")] IReadOnlyList<RescueCell> Entries);
}
