using System.Text.Json;
using System.Text.Json.Serialization;

namespace SilentScan.Core.Rules;

public sealed record SargShapeCell(string Shape, string ColumnType, string Outcome);

public sealed class SargShapeMatrix
{
    public const string SeekOutcome = "Seek";

    public const string ScanOutcome = "Scan";

    public const string ErrorPrefix = "Error:";

    public static SargShapeMatrix Instance { get; } = LoadEmbedded();

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly Dictionary<(string Shape, string ColumnType), SargShapeCell> _byKey;

    internal SargShapeMatrix(string serverVersion, string probedAtUtc, IReadOnlyList<SargShapeCell> entries)
    {
        ServerVersion = serverVersion;
        ProbedAtUtc = probedAtUtc;
        Entries = entries;
        _byKey = entries.ToDictionary(e => (e.Shape, e.ColumnType.ToUpperInvariant()));
    }

    public string ServerVersion { get; }

    public string ProbedAtUtc { get; }

    public IReadOnlyList<SargShapeCell> Entries { get; }

    public string? TryGetOutcome(string shape, string columnType) =>
        _byKey.TryGetValue((shape, columnType.ToUpperInvariant()), out var cell) ? cell.Outcome : null;

    public bool? PreservesSeek(string shape, string columnType) =>
        TryGetOutcome(shape, columnType) is { } outcome
            ? outcome.StartsWith(SeekOutcome, StringComparison.Ordinal)
            : null;

    private static SargShapeMatrix LoadEmbedded()
    {
        var assembly = typeof(SargShapeMatrix).Assembly;
        var resourceName = $"{assembly.GetName().Name}.Rules.SargShapeMatrix.json";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found - the matrix data is missing from the build.");

        var document = JsonSerializer.Deserialize<MatrixDocument>(stream, JsonOptions)
            ?? throw new InvalidOperationException("SargShapeMatrix.json deserialized to null.");

        return new SargShapeMatrix(document.ServerVersion, document.ProbedAtUtc, document.Entries);
    }

    private sealed record MatrixDocument(
        [property: JsonPropertyName("ServerVersion")] string ServerVersion,
        [property: JsonPropertyName("ProbedAtUtc")] string ProbedAtUtc,
        [property: JsonPropertyName("Notes")] string Notes,
        [property: JsonPropertyName("Entries")] IReadOnlyList<SargShapeCell> Entries);
}
