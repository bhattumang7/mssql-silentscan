namespace SilentScan.Core.Catalog;

public sealed record CatalogStatisticsInfo(
    string Name,
    bool NoRecompute,
    bool IsAutoCreated,
    IReadOnlyList<string>? KeyColumnsRaw = null,
    string? FilterDefinition = null)
{
    public IReadOnlyList<string> KeyColumns => KeyColumnsRaw ?? [];
}
