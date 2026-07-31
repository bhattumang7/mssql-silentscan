using System.Text.Json.Serialization;
using SilentScan.Core.Catalog;

namespace SilentScan.Core.Lineage;

/// <summary>
/// Where a view/TVF output column's type ultimately comes from (CLAUDE.md Pass 2):
/// BaseColumn | Expression | Cast | Unknown, plus Union for UNION/UNION ALL branches
/// which must record every branch's provenance rather than collapsing them.
/// </summary>
[JsonPolymorphic]
[JsonDerivedType(typeof(BaseColumn), "BaseColumn")]
[JsonDerivedType(typeof(Declared), "Declared")]
[JsonDerivedType(typeof(Cast), "Cast")]
[JsonDerivedType(typeof(Expression), "Expression")]
[JsonDerivedType(typeof(Union), "Union")]
[JsonDerivedType(typeof(Unknown), "Unknown")]
public abstract record ColumnProvenance
{
    private ColumnProvenance()
    {
    }

    /// <summary>
    /// A direct passthrough of a physical table column, resolved through however many view
    /// layers sit between. Depth counts those layers (0 = the predicate reads the table
    /// directly; N = N views/TVFs sit between) - CLAUDE.md's "depth" finding field.
    /// </summary>
    public sealed record BaseColumn(string TableQualifiedName, string ColumnName, SqlType? Type, int Depth = 0) : ColumnProvenance;

    /// <summary>A declared type that isn't traced further - e.g. a multi-statement TVF's RETURNS TABLE(...) column.</summary>
    public sealed record Declared(SqlType Type) : ColumnProvenance;

    /// <summary>
    /// An explicit CAST/CONVERT to a named type. Origin is where the CAST itself appears -
    /// CLAUDE.md's "origin: file/line of the layer that introduced the mismatch (e.g., the
    /// CAST inside vw_X)" - distinct from the predicate's own location.
    /// </summary>
    public sealed record Cast(SqlType ExplicitType, string? OriginSourcePath = null, int OriginLine = 0, int Depth = 0) : ColumnProvenance;

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
