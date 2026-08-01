using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace SilentScan.Core.Corpus;

/// <summary>Loads and validates corpus/manifest.json (CLAUDE.md: "repo URL, commit SHA pinned, license, ...").</summary>
public static partial class CorpusManifestLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [GeneratedRegex("^[0-9a-f]{40}$")]
    private static partial Regex CommitShaPattern();

    public static CorpusManifest Load(string path)
    {
        var json = File.ReadAllText(path);
        return Parse(json);
    }

    public static CorpusManifest Parse(string json)
    {
        var dto = JsonSerializer.Deserialize<ManifestDto>(json, JsonOptions)
            ?? throw new InvalidDataException("Manifest deserialized to null.");

        var repos = dto.Repos.Select(ValidateAndConvert).ToList();
        return new CorpusManifest(repos);
    }

    private static CorpusRepoEntry ValidateAndConvert(RepoDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
        {
            throw new InvalidDataException("A corpus manifest entry is missing 'name'.");
        }

        if (string.IsNullOrWhiteSpace(dto.CommitSha) || !CommitShaPattern().IsMatch(dto.CommitSha))
        {
            throw new InvalidDataException($"'{dto.Name}': commitSha must be a full 40-character lowercase hex SHA, never a branch name (CLAUDE.md: pin the commit).");
        }

        if (string.IsNullOrWhiteSpace(dto.License))
        {
            throw new InvalidDataException($"'{dto.Name}': license is required before a repo can be scanned.");
        }

        if (dto.DdlPaths is not { Count: > 0 })
        {
            throw new InvalidDataException($"'{dto.Name}': ddlPaths is empty - a corpus entry with no declared DDL paths can't be scanned meaningfully.");
        }

        return new CorpusRepoEntry(
            dto.Name,
            dto.Url,
            dto.CommitSha,
            dto.License,
            dto.DdlPaths,
            dto.ProcPaths ?? [],
            dto.DeclaredCollation,
            dto.Notes);
    }

    private sealed record ManifestDto([property: JsonPropertyName("repos")] IReadOnlyList<RepoDto> Repos);

    private sealed record RepoDto(
        string Name,
        string Url,
        string CommitSha,
        string License,
        IReadOnlyList<string>? DdlPaths,
        IReadOnlyList<string>? ProcPaths,
        string? DeclaredCollation,
        string? Notes);
}
