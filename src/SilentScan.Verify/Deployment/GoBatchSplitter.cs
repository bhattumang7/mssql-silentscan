using System.Text.RegularExpressions;

namespace SilentScan.Verify.Deployment;

/// <summary>
/// Splits a .sql script on GO batch separators. GO is a client-side convention (sqlcmd/SSMS),
/// not T-SQL grammar, so it must be handled before anything reaches the server - ScriptDOM
/// itself already batches on GO when parsing, but the raw deploy path here works directly
/// against SqlClient and needs its own split.
/// </summary>
public static partial class GoBatchSplitter
{
    // The repeat-count group is non-capturing: Regex.Split() would otherwise emit captured
    // groups as extra array elements interleaved with the real batches.
    [GeneratedRegex(@"^\s*GO\s*(?:\d+)?\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex GoSeparator();

    public static IReadOnlyList<string> Split(string script)
    {
        return GoSeparator().Split(script)
            .Select(batch => batch.Trim())
            .Where(batch => batch.Length > 0)
            .ToList();
    }
}
