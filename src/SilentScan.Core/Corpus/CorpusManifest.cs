namespace SilentScan.Core.Corpus;

public sealed record CorpusManifest(IReadOnlyList<CorpusRepoEntry> Repos);
