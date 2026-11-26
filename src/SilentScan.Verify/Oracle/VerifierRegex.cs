using System.Text.RegularExpressions;

namespace SilentScan.Verify.Oracle;

[VerifierSupport]
internal static class VerifierRegex
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(5);

    public static Regex Create(string pattern, RegexOptions options = RegexOptions.None) =>
        new(pattern, options, MatchTimeout);

    public static MatchCollection Matches(string input, string pattern, RegexOptions options = RegexOptions.None) =>
        Regex.Matches(input, pattern, options, MatchTimeout);

    public static bool IsMatch(string input, string pattern, RegexOptions options = RegexOptions.None) =>
        Regex.IsMatch(input, pattern, options, MatchTimeout);

    public static string Replace(string input, string pattern, string replacement, RegexOptions options = RegexOptions.None) =>
        Regex.Replace(input, pattern, replacement, options, MatchTimeout);
}
