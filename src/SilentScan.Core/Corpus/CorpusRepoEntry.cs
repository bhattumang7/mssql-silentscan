namespace SilentScan.Core.Corpus;

/// <summary>One pinned repo entry in corpus/manifest.json (CLAUDE.md corpus rules).</summary>
public sealed record CorpusRepoEntry(
    string Name,
    string Url,
    string CommitSha,
    string License,
    IReadOnlyList<string> DdlPaths,
    IReadOnlyList<string> ProcPaths,
    string? DeclaredCollation,
    string? Notes);
