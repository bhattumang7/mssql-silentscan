namespace SilentScan.Core.Catalog;

public enum CatalogTableKind
{
    Table,
    TemporaryTable,
    TableVariable,
}

public sealed record CatalogTable(
    string? SchemaName,
    string Name,
    CatalogTableKind Kind,
    IReadOnlyList<CatalogColumn> Columns,
    IReadOnlyList<CatalogIndex> Indexes,
    string SourcePath,
    int SourceLine)
{
    /// <summary>schema.name, or just name for temp tables/table variables which have no schema.</summary>
    public string QualifiedName => SchemaName is null ? Name : $"{SchemaName}.{Name}";

    public CatalogColumn? FindColumn(string columnName) =>
        Columns.FirstOrDefault(c => string.Equals(c.Name, columnName, StringComparison.OrdinalIgnoreCase));

    public bool IsIndexedColumn(string columnName) =>
        Indexes.Any(i => i.KeyColumns.Any(k => string.Equals(k, columnName, StringComparison.OrdinalIgnoreCase)));
}
