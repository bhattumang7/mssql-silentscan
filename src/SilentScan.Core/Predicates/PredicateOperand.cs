using System.Text.Json.Serialization;
using SilentScan.Core.Catalog;
using SilentScan.Core.Lineage;

namespace SilentScan.Core.Predicates;

/// <summary>One side of a `colRef &lt;op&gt; other` comparison, typed for the verdict engine (CLAUDE.md Pass 3).</summary>
[JsonPolymorphic]
[JsonDerivedType(typeof(Column), "Column")]
[JsonDerivedType(typeof(Value), "Value")]
public abstract record PredicateOperand
{
    private PredicateOperand()
    {
    }

    /// <summary>A column resolved (however many view layers deep) to a real base table column.</summary>
    public sealed record Column(string TableQualifiedName, string ColumnName, SqlType? Type, bool Indexed, int Depth, ColumnProvenance Provenance) : PredicateOperand;

    /// <summary>A literal, parameter/variable, or non-column expression - typed if we could, untyped (null) if not.</summary>
    public sealed record Value(SqlType? Type) : PredicateOperand;
}
