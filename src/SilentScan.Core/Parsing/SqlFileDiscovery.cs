namespace SilentScan.Core.Parsing;

/// <summary>Finds .sql files under a corpus root. Ordering is deterministic (CLAUDE.md).</summary>
public static class SqlFileDiscovery
{
    public static IReadOnlyList<string> EnumerateSqlFiles(string rootPath)
    {
        if (File.Exists(rootPath))
        {
            return [rootPath];
        }

        return Directory.EnumerateFiles(rootPath, "*.sql", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
    }
}
