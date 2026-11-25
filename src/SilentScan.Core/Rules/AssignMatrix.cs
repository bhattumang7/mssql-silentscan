using System.Text.Json;
using System.Text.Json.Serialization;

namespace SilentScan.Core.Rules;

public enum AssignContext
{
    Variable,

    ColumnAnsiWarningsOn,

    ColumnAnsiWarningsOff,
}

public sealed record AssignCellOutcome(
    string Source,
    string Target,
    IReadOnlyList<string> Variable,
    IReadOnlyList<string> ColumnAnsiWarningsOn,
    IReadOnlyList<string> ColumnAnsiWarningsOff)
{
    public const string EqualOutcome = "Equal";

    public const string LossOutcome = "Loss";

    public const string ErrorPrefix = "Error:";

    public IReadOnlyList<string> For(AssignContext context) => context switch
    {
        AssignContext.Variable => Variable,
        AssignContext.ColumnAnsiWarningsOn => ColumnAnsiWarningsOn,
        AssignContext.ColumnAnsiWarningsOff => ColumnAnsiWarningsOff,
        _ => throw new ArgumentOutOfRangeException(nameof(context)),
    };

    public bool SilentlyLoses(AssignContext context) =>
        For(context).Contains(LossOutcome, StringComparer.Ordinal);

    public bool IsLoud(AssignContext context) =>
        For(context).Any(o => o.StartsWith(ErrorPrefix, StringComparison.Ordinal));
}

public sealed class AssignMatrix
{
    public static AssignMatrix Instance { get; } = LoadEmbedded();

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly Dictionary<(string Source, string Target), AssignCellOutcome> _byKey;

    internal AssignMatrix(string serverVersion, string probedAtUtc, IReadOnlyList<AssignCellOutcome> entries)
    {
        ServerVersion = serverVersion;
        ProbedAtUtc = probedAtUtc;
        Entries = entries;
        _byKey = entries.ToDictionary(e => (e.Source, e.Target));
    }

    public string ServerVersion { get; }

    public string ProbedAtUtc { get; }

    public IReadOnlyList<AssignCellOutcome> Entries { get; }

    public AssignCellOutcome? TryGet(string source, string target) =>
        _byKey.TryGetValue((source.ToUpperInvariant(), target.ToUpperInvariant()), out var cell) ? cell : null;

    private static AssignMatrix LoadEmbedded()
    {
        var assembly = typeof(AssignMatrix).Assembly;
        var resourceName = $"{assembly.GetName().Name}.Rules.AssignMatrix.json";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found - the matrix data is missing from the build.");

        var document = JsonSerializer.Deserialize<MatrixDocument>(stream, JsonOptions)
            ?? throw new InvalidOperationException("AssignMatrix.json deserialized to null.");

        var entries = document.Entries
            .Select(e => new AssignCellOutcome(e.Source, e.Target, e.Variable, e.ColumnAnsiWarningsOn, e.ColumnAnsiWarningsOff))
            .ToList();

        return new AssignMatrix(document.ServerVersion, document.ProbedAtUtc, entries);
    }

    private sealed record MatrixDocument(
        [property: JsonPropertyName("ServerVersion")] string ServerVersion,
        [property: JsonPropertyName("ProbedAtUtc")] string ProbedAtUtc,
        [property: JsonPropertyName("Notes")] string Notes,
        [property: JsonPropertyName("Entries")] IReadOnlyList<AssignCellOutcome> Entries);
}
