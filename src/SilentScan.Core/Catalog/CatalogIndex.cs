namespace SilentScan.Core.Catalog;

public sealed record CatalogIndex(
    string? Name,
    CatalogIndexKind Kind,
    bool IsUnique,
    IReadOnlyList<string> KeyColumns,
    IReadOnlyList<string> IncludedColumns);
