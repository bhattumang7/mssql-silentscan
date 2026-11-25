using System.Text.Json;
using System.Text.Json.Serialization;

namespace SilentScan.Core.Rules;

public sealed record BuiltinCell(
    string Function,
    string Shape,
    string ResultType,
    string? Collation,
    bool? Nullable,
    string Deterministic,
    string Precise,
    string Parallel);

public sealed class BuiltinMatrix
{
    public const string ErrorPrefix = "Error:";

    public const string ParallelOutcome = "Parallel";

    public const string NoSerialReasonOutcome = "NoSerialReason";

    public static BuiltinMatrix Instance { get; } = LoadEmbedded();

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly ILookup<string, BuiltinCell> _byFunction;

    internal BuiltinMatrix(string serverVersion, string probedAtUtc, IReadOnlyList<BuiltinCell> entries)
    {
        ServerVersion = serverVersion;
        ProbedAtUtc = probedAtUtc;
        Entries = entries;
        _byFunction = entries.ToLookup(e => e.Function, StringComparer.OrdinalIgnoreCase);
    }

    public string ServerVersion { get; }

    public string ProbedAtUtc { get; }

    public IReadOnlyList<BuiltinCell> Entries { get; }

    public IReadOnlyList<BuiltinCell> ForFunction(string function) => [.. _byFunction[function]];

    public BuiltinCell? TryGet(string function, string shape) =>
        _byFunction[function].FirstOrDefault(c => string.Equals(c.Shape, shape, StringComparison.OrdinalIgnoreCase));

    private static BuiltinMatrix LoadEmbedded()
    {
        var assembly = typeof(BuiltinMatrix).Assembly;
        var resourceName = $"{assembly.GetName().Name}.Rules.BuiltinMatrix.json";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found - the matrix data is missing from the build.");

        var document = JsonSerializer.Deserialize<MatrixDocument>(stream, JsonOptions)
            ?? throw new InvalidOperationException("BuiltinMatrix.json deserialized to null.");

        return new BuiltinMatrix(document.ServerVersion, document.ProbedAtUtc, document.Entries);
    }

    private sealed record MatrixDocument(
        [property: JsonPropertyName("ServerVersion")] string ServerVersion,
        [property: JsonPropertyName("ProbedAtUtc")] string ProbedAtUtc,
        [property: JsonPropertyName("Notes")] string Notes,
        [property: JsonPropertyName("Entries")] IReadOnlyList<BuiltinCell> Entries);
}
