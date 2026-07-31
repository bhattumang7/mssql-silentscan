namespace SilentScan.Core.Lineage;

/// <summary>A FROM-clause-able thing (base table, resolved view, or derived subquery) with its resolved output columns.</summary>
public sealed record ResolvedRelation(string? QualifiedName, IReadOnlyList<ResolvedColumn> Columns)
{
    public static readonly ResolvedRelation Empty = new(QualifiedName: null, Columns: []);

    public ResolvedColumn? FindColumn(string columnName) =>
        Columns.FirstOrDefault(c => string.Equals(c.Name, columnName, StringComparison.OrdinalIgnoreCase));
}
