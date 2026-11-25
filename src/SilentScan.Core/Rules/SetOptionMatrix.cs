using System.Text.Json;
using System.Text.Json.Serialization;

namespace SilentScan.Core.Rules;

public sealed record SetOptionCell(string Id, string Option, string Construct, string WhenOn, string WhenOff)
{
    public bool Differs => !string.Equals(WhenOn, WhenOff, StringComparison.Ordinal);
}

public sealed class SetOptionMatrix
{
    public static SetOptionMatrix Instance { get; } = LoadEmbedded();

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly Dictionary<string, SetOptionCell> _byId;

    internal SetOptionMatrix(string serverVersion, string probedAtUtc, IReadOnlyList<SetOptionCell> entries)
    {
        ServerVersion = serverVersion;
        ProbedAtUtc = probedAtUtc;
        Entries = entries;
        _byId = entries.ToDictionary(e => e.Id);
    }

    public string ServerVersion { get; }

    public string ProbedAtUtc { get; }

    public IReadOnlyList<SetOptionCell> Entries { get; }

    public SetOptionCell? TryGet(string id) => _byId.GetValueOrDefault(id);

    public IEnumerable<SetOptionCell> ForOption(string option) =>
        Entries.Where(e => string.Equals(e.Option, option, StringComparison.OrdinalIgnoreCase));

    private static SetOptionMatrix LoadEmbedded()
    {
        var assembly = typeof(SetOptionMatrix).Assembly;
        var resourceName = $"{assembly.GetName().Name}.Rules.SetOptionMatrix.json";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found - the matrix data is missing from the build.");

        var document = JsonSerializer.Deserialize<MatrixDocument>(stream, JsonOptions)
            ?? throw new InvalidOperationException("SetOptionMatrix.json deserialized to null.");

        return new SetOptionMatrix(document.ServerVersion, document.ProbedAtUtc, document.Entries);
    }

    private sealed record MatrixDocument(
        [property: JsonPropertyName("ServerVersion")] string ServerVersion,
        [property: JsonPropertyName("ProbedAtUtc")] string ProbedAtUtc,
        [property: JsonPropertyName("Notes")] string Notes,
        [property: JsonPropertyName("Entries")] IReadOnlyList<SetOptionCell> Entries);
}
