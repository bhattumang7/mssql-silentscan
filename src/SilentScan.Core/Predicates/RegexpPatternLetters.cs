using System.Text.RegularExpressions;

namespace SilentScan.Core.Predicates;

internal static partial class RegexpPatternLetters
{
    [GeneratedRegex(@"\\[dDsSwWbBnrtfvaeAzZGhHRX]|\\[^A-Za-z]|\[:(?:alpha|alnum|digit|space|punct|word|xdigit|blank|cntrl|graph|print|ascii):\]|\(\?P?<[A-Za-z_][A-Za-z0-9_]*>")]
    private static partial Regex CaseNeutralTokens { get; }

    [GeneratedRegex(@"\\[xXpPuUcNo]|\[:(?:upper|lower):\]")]
    private static partial Regex CaseCarryingTokens { get; }

    public static bool HasCaseDependentLetter(string pattern) =>
        CaseCarryingTokens.IsMatch(pattern) || CaseNeutralTokens.Replace(pattern, string.Empty).Any(char.IsAsciiLetter);

    public static bool HasLiteralLetter(string pattern) =>
        CaseNeutralTokens.Replace(CaseCarryingTokens.Replace(pattern, string.Empty), string.Empty).Any(char.IsLetter);
}
