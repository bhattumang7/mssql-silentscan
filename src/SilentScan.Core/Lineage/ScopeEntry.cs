namespace SilentScan.Core.Lineage;

/// <summary>
/// A resolved FROM-clause entry plus whether reading from it crosses a persisted view/TVF
/// boundary (as opposed to a base table or an inline derived-table subquery) - this is what
/// depth-tracking increments on (CLAUDE.md "depth: N = layers of views/TVFs").
/// </summary>
public readonly record struct ScopeEntry(ResolvedRelation Relation, bool IsViewLayer);
