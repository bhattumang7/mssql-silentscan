namespace SilentScan.Core.Catalog;

/// <summary>All tables/views/temp tables/table variables discovered across a scanned folder (Pass 1 output).</summary>
public sealed class DatabaseCatalog
{
    private readonly Dictionary<string, CatalogTable> _tablesByQualifiedName =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<CatalogTable> Tables => _tablesByQualifiedName.Values;

    public Collation? DefaultCollation { get; set; }

    public void AddOrReplace(CatalogTable table) => _tablesByQualifiedName[table.QualifiedName] = table;

    public CatalogTable? Find(string qualifiedName) =>
        _tablesByQualifiedName.GetValueOrDefault(qualifiedName);
}
