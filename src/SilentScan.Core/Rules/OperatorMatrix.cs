using System.Text.Json;
using System.Text.Json.Serialization;

namespace SilentScan.Core.Rules;

public sealed record OperatorCell(string Left, string Right, IReadOnlyList<string> Results);

public sealed class OperatorMatrix
{
    public const string ErrorPrefix = "Error:";

    public static OperatorMatrix Instance { get; } = LoadEmbedded();

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly Dictionary<(string Left, string Right), OperatorCell> _byKey;
    private readonly Dictionary<string, int> _operatorIndex;

    internal OperatorMatrix(string serverVersion, string probedAtUtc, IReadOnlyList<string> operators, IReadOnlyList<OperatorCell> entries)
    {
        ServerVersion = serverVersion;
        ProbedAtUtc = probedAtUtc;
        Operators = operators;
        Entries = entries;
        _byKey = entries.ToDictionary(e => (e.Left, e.Right));
        _operatorIndex = operators.Select((o, i) => (o, i)).ToDictionary(x => x.o, x => x.i, StringComparer.OrdinalIgnoreCase);
    }

    public string ServerVersion { get; }

    public string ProbedAtUtc { get; }

    public IReadOnlyList<string> Operators { get; }

    public IReadOnlyList<OperatorCell> Entries { get; }

    public string? TryGetResult(string op, string left, string right)
    {
        if (!_operatorIndex.TryGetValue(op, out var index)
            || !_byKey.TryGetValue((left.ToUpperInvariant(), right.ToUpperInvariant()), out var cell))
        {
            return null;
        }

        return cell.Results[index];
    }

    private static OperatorMatrix LoadEmbedded()
    {
        var assembly = typeof(OperatorMatrix).Assembly;
        var resourceName = $"{assembly.GetName().Name}.Rules.OperatorMatrix.json";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found - the matrix data is missing from the build.");

        var document = JsonSerializer.Deserialize<MatrixDocument>(stream, JsonOptions)
            ?? throw new InvalidOperationException("OperatorMatrix.json deserialized to null.");

        return new OperatorMatrix(document.ServerVersion, document.ProbedAtUtc, document.Operators, document.Entries);
    }

    private sealed record MatrixDocument(
        [property: JsonPropertyName("ServerVersion")] string ServerVersion,
        [property: JsonPropertyName("ProbedAtUtc")] string ProbedAtUtc,
        [property: JsonPropertyName("Notes")] string Notes,
        [property: JsonPropertyName("Operators")] IReadOnlyList<string> Operators,
        [property: JsonPropertyName("Entries")] IReadOnlyList<OperatorCell> Entries);
}
