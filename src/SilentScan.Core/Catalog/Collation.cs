namespace SilentScan.Core.Catalog;

/// <summary>
/// A SQL Server collation name and the family it belongs to. The family determines
/// whether an implicit conversion between varchar and nvarchar forces a scan
/// (SQL_* legacy collations) or permits a dynamic range seek (Windows collations).
/// </summary>
public sealed record Collation(string Name)
{
    /// <summary>
    /// SQL_* collations (the legacy Sybase-derived family, e.g. SQL_Latin1_General_CP1_CI_AS)
    /// cannot build GetRangeThroughConvert for a varchar/nvarchar mismatch: the predicate
    /// forces a full scan. Windows collations (e.g. Latin1_General_CI_AS) can.
    /// </summary>
    public bool IsSqlFamily => Name.StartsWith("SQL_", StringComparison.OrdinalIgnoreCase);

    public bool IsWindowsFamily => !IsSqlFamily;
}
