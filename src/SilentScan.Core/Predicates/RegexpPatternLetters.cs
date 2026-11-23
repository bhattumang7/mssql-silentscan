using System.Text.RegularExpressions;

namespace SilentScan.Core.Predicates;

internal static partial class RegexpPatternLetters
{
    [GeneratedRegex(@"\\[dDsSwWbBnrtfvaeAzZGhHRX]|\\[^A-Za-z]|\[:(?:alpha|alnum|digit|space|punct|word|xdigit|blank|cntrl|graph|print|ascii):\]|\(\?P?<[A-Za-z_][A-Za-z0-9_]*>|\(\?[imsU-]+[:)]")]
    private static partial Regex CaseNeutralTokens { get; }

    [GeneratedRegex(@"\\[pP]\{\^?(?:Lu|Ll|Lt|Upper|Lower)\}|\\[xXuUcNo]|\[:(?:upper|lower):\]")]
    private static partial Regex CaseCarryingTokens { get; }

    [GeneratedRegex(@"\\[pP](?:\{\^?[A-Za-z_&]+\}|[A-Za-z])")]
    private static partial Regex PropertyClasses { get; }

    [GeneratedRegex(@"\(\?[ms]*i[ims]*[:)]")]
    private static partial Regex CaseInsensitiveFlag { get; }

    [GeneratedRegex(@"\[\^?((?:[A-Za-z0-9_ ]|[A-Za-z]-[A-Za-z]|[0-9]-[0-9])+)\]")]
    private static partial Regex SimpleBracketClass { get; }

    public static bool HasCaseDependentLetter(string pattern)
    {
        if (CaseInsensitiveFlag.IsMatch(pattern))
        {
            return false;
        }

        var stripped = StripCaseClosedClasses(pattern);
        stripped = PropertyClasses.Replace(stripped, match => CaseCarryingTokens.IsMatch(match.Value) ? match.Value : string.Empty);
        return CaseCarryingTokens.IsMatch(stripped) || CaseNeutralTokens.Replace(stripped, string.Empty).Any(char.IsAsciiLetter);
    }

    public static bool HasLiteralLetter(string pattern)
    {
        var withoutProperties = PropertyClasses.Replace(pattern, string.Empty);
        return CaseNeutralTokens.Replace(CaseCarryingTokens.Replace(withoutProperties, string.Empty), string.Empty).Any(char.IsLetter);
    }

    private static string StripCaseClosedClasses(string pattern) =>
        SimpleBracketClass.Replace(pattern, match => IsCaseClosed(match.Groups[1].Value) ? string.Empty : match.Value);

    private static bool IsCaseClosed(string body)
    {
        var letters = new HashSet<char>();
        for (var i = 0; i < body.Length; i++)
        {
            if (i + 2 < body.Length && body[i + 1] == '-' && char.IsAsciiLetter(body[i]) && char.IsAsciiLetter(body[i + 2]) && body[i] <= body[i + 2])
            {
                for (var c = body[i]; c <= body[i + 2]; c++)
                {
                    letters.Add(c);
                }

                i += 2;
            }
            else if (char.IsAsciiLetter(body[i]))
            {
                letters.Add(body[i]);
            }
        }

        return letters.All(letter => letters.Contains(char.IsAsciiLetterLower(letter) ? char.ToUpperInvariant(letter) : char.ToLowerInvariant(letter)));
    }
}
