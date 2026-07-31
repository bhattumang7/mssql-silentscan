using SilentScan.Core.Catalog;

namespace SilentScan.Core.Lineage;

/// <summary>
/// Where a view/TVF output column's type ultimately comes from (CLAUDE.md Pass 2):
/// BaseColumn | Expression | Cast | Unknown, plus Union for UNION/UNION ALL branches
/// which must record every branch's provenance rather than collapsing them.
/// </summary>
public abstract record ColumnProvenance
{
    private ColumnProvenance()
    {
    }

    /// <summary>A direct passthrough of a physical table column, resolved through however many view layers sit between.</summary>
    public sealed record BaseColumn(string TableQualifiedName, string ColumnName, SqlType? Type) : ColumnProvenance;

    /// <summary>A declared type that isn't traced further - e.g. a multi-statement TVF's RETURNS TABLE(...) column.</summary>
    public sealed record Declared(SqlType Type) : ColumnProvenance;

    /// <summary>An explicit CAST/CONVERT to a named type.</summary>
    public sealed record Cast(SqlType ExplicitType) : ColumnProvenance;

    /// <summary>Any other scalar expression (function call, arithmetic, CASE, literal, ...). InferredType is null when we didn't attempt to type it.</summary>
    public sealed record Expression(SqlType? InferredType) : ColumnProvenance;

    /// <summary>
    /// A UNION/UNION ALL/EXCEPT/INTERSECT output column. CLAUDE.md: "record ALL branch
    /// types - the mixed-branch case is itself a finding," so branches are kept, not merged.
    /// </summary>
    public sealed record Union(IReadOnlyList<ColumnProvenance> Branches) : ColumnProvenance;

    /// <summary>Could not be resolved; never guess (CLAUDE.md precision discipline).</summary>
    public sealed record Unknown(string Reason) : ColumnProvenance;
}
