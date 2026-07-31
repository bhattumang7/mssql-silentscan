namespace SilentScan.Core.Lineage;

/// <summary>Pass 2 output: every view/inline-TVF/multi-statement-TVF, resolved to its output columns' provenance.</summary>
public sealed class LineageCatalog(IReadOnlyDictionary<string, ResolvedRelation> relationsByQualifiedName, IReadOnlySet<string> cyclicViews)
{
    public IReadOnlySet<string> CyclicViews { get; } = cyclicViews;

    /// <summary>All resolved views/TVFs, for callers (e.g. Pass 3's predicate extractor) that need to build their own FROM scopes against them.</summary>
    public IReadOnlyDictionary<string, ResolvedRelation> AllRelations => relationsByQualifiedName;

    public ResolvedRelation? Find(string qualifiedName) =>
        relationsByQualifiedName.GetValueOrDefault(qualifiedName);
}
