using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SilentScan.Core.Rules;

public sealed record GuessCell(string Shape, string ColumnType, string Operand, string Estimator, string Ratio);

public sealed class GuessMatrix
{
    public const string ErrorPrefix = "Error:";

    public const string NoTableScan = "NoTableScan";

    public static GuessMatrix Instance { get; } = LoadEmbedded();

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly Dictionary<(string Shape, string ColumnType, string Operand, string Estimator), GuessCell> _byKey;

    internal GuessMatrix(string serverVersion, string probedAtUtc, long tableRows, IReadOnlyList<GuessCell> entries)
    {
        ServerVersion = serverVersion;
        ProbedAtUtc = probedAtUtc;
        TableRows = tableRows;
        Entries = entries;
        _byKey = entries.ToDictionary(e => (e.Shape, e.ColumnType.ToUpperInvariant(), e.Operand, e.Estimator));
    }

    public string ServerVersion { get; }

    public string ProbedAtUtc { get; }

    public long TableRows { get; }

    public IReadOnlyList<GuessCell> Entries { get; }

    public double? TryGetRatio(string shape, string columnType, string operand, string estimator) =>
        _byKey.TryGetValue((shape, columnType.ToUpperInvariant(), operand, estimator), out var cell)
        && double.TryParse(cell.Ratio, NumberStyles.Float, CultureInfo.InvariantCulture, out var ratio)
            ? ratio
            : null;

    private static GuessMatrix LoadEmbedded()
    {
        var assembly = typeof(GuessMatrix).Assembly;
        var resourceName = $"{assembly.GetName().Name}.Rules.GuessMatrix.json";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found - the matrix data is missing from the build.");

        var document = JsonSerializer.Deserialize<MatrixDocument>(stream, JsonOptions)
            ?? throw new InvalidOperationException("GuessMatrix.json deserialized to null.");

        return new GuessMatrix(document.ServerVersion, document.ProbedAtUtc, document.TableRows, document.Entries);
    }

    private sealed record MatrixDocument(
        [property: JsonPropertyName("ServerVersion")] string ServerVersion,
        [property: JsonPropertyName("ProbedAtUtc")] string ProbedAtUtc,
        [property: JsonPropertyName("Notes")] string Notes,
        [property: JsonPropertyName("TableRows")] long TableRows,
        [property: JsonPropertyName("Entries")] IReadOnlyList<GuessCell> Entries);
}
