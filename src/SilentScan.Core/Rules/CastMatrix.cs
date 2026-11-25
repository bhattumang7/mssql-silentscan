using System.Text.Json;
using System.Text.Json.Serialization;

namespace SilentScan.Core.Rules;

public enum CastContext
{
    Cast,

    TryCast,
}

public sealed record CastCellOutcome(string Source, string Target, IReadOnlyList<string> Cast, IReadOnlyList<string> TryCast)
{
    public const string EqualOutcome = "Equal";

    public const string LossOutcome = "Loss";

    public const string NullOutcome = "Null";

    public const string ErrorPrefix = "Error:";

    public IReadOnlyList<string> For(CastContext context) => context switch
    {
        CastContext.Cast => Cast,
        CastContext.TryCast => TryCast,
        _ => throw new ArgumentOutOfRangeException(nameof(context)),
    };

    public bool SilentlyLoses(CastContext context) =>
        For(context).Contains(LossOutcome, StringComparer.Ordinal);

    public bool IsLoud(CastContext context) =>
        For(context).Any(o => o.StartsWith(ErrorPrefix, StringComparison.Ordinal));
}

public sealed record CastResultType(string Target, string OfLiteral, string OfVariable);

public sealed class CastMatrix
{
    public static CastMatrix Instance { get; } = LoadEmbedded();

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly Dictionary<(string Source, string Target), CastCellOutcome> _byKey;
    private readonly Dictionary<string, CastResultType> _resultByTarget;

    internal CastMatrix(string serverVersion, string probedAtUtc, IReadOnlyList<CastCellOutcome> entries, IReadOnlyList<CastResultType> resultTypes)
    {
        ServerVersion = serverVersion;
        ProbedAtUtc = probedAtUtc;
        Entries = entries;
        ResultTypes = resultTypes;
        _byKey = entries.ToDictionary(e => (e.Source, e.Target));
        _resultByTarget = resultTypes.ToDictionary(r => r.Target);
    }

    public string ServerVersion { get; }

    public string ProbedAtUtc { get; }

    public IReadOnlyList<CastCellOutcome> Entries { get; }

    public IReadOnlyList<CastResultType> ResultTypes { get; }

    public CastCellOutcome? TryGet(string source, string target) =>
        _byKey.TryGetValue((source.ToUpperInvariant(), target.ToUpperInvariant()), out var cell) ? cell : null;

    public CastResultType? TryGetResultType(string target) =>
        _resultByTarget.TryGetValue(target.ToUpperInvariant(), out var result) ? result : null;

    private static CastMatrix LoadEmbedded()
    {
        var assembly = typeof(CastMatrix).Assembly;
        var resourceName = $"{assembly.GetName().Name}.Rules.CastMatrix.json";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found - the matrix data is missing from the build.");

        var document = JsonSerializer.Deserialize<MatrixDocument>(stream, JsonOptions)
            ?? throw new InvalidOperationException("CastMatrix.json deserialized to null.");

        return new CastMatrix(document.ServerVersion, document.ProbedAtUtc, document.Entries, document.ResultTypes);
    }

    private sealed record MatrixDocument(
        [property: JsonPropertyName("ServerVersion")] string ServerVersion,
        [property: JsonPropertyName("ProbedAtUtc")] string ProbedAtUtc,
        [property: JsonPropertyName("Notes")] string Notes,
        [property: JsonPropertyName("Entries")] IReadOnlyList<CastCellOutcome> Entries,
        [property: JsonPropertyName("ResultTypes")] IReadOnlyList<CastResultType> ResultTypes);
}
