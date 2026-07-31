namespace SilentScan.Core.Lineage;

/// <summary>Pass 2 output: every view/inline-TVF/multi-statement-TVF, resolved to its output columns' provenance.</summary>
public sealed class LineageCatalog(IReadOnlyDictionary<string, ResolvedRelation> relationsByQualifiedName, IReadOnlySet<string> cyclicViews)
{
    public IReadOnlySet<string> CyclicViews { get; } = cyclicViews;

    public ResolvedRelation? Find(string qualifiedName) =>
        relationsByQualifiedName.GetValueOrDefault(qualifiedName);
}
