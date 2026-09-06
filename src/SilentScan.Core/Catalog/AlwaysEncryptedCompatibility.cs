namespace SilentScan.Core.Catalog;

public static class AlwaysEncryptedCompatibility
{
    public static bool IsMismatch(CatalogColumn first, CatalogColumn second, StringComparer identifierComparer)
    {
        if (first.EncryptionType != second.EncryptionType)
        {
            return true;
        }

        return first.EncryptionType != ColumnEncryptionType.None
            && !identifierComparer.Equals(first.EncryptionKeyName, second.EncryptionKeyName);
    }

    public static string FormatDisplay(CatalogColumn column) =>
        column.EncryptionKeyName is { } keyName
            ? $"{column.EncryptionType} key {keyName}"
            : column.EncryptionType.ToString();
}
