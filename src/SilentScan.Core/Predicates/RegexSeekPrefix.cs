namespace SilentScan.Core.Predicates;

internal static class RegexSeekPrefix
{
    private const string StoppingMetacharacters = "$.()|^]}-";

    private const string EscapesWithoutALiteralRange = "abdfnprstvwxzABCDEPQSW";

    public static bool CanBeDerived(string pattern)
    {
        if (pattern.Length == 0 || pattern[0] != '^')
        {
            return false;
        }

        var literalAtoms = 0;
        for (var i = 1; i < pattern.Length; i++)
        {
            switch (pattern[i])
            {
                case '[':
                    return !ContainsNamedClass(pattern, i) && (!IsNegatedClass(pattern, i) || literalAtoms > 0);
                case '\\' when i + 1 < pattern.Length && !EscapesWithoutALiteralRange.Contains(pattern[i + 1], StringComparison.Ordinal):
                    literalAtoms++;
                    i++;
                    break;
                case '\\':
                    return false;
                case '*' or '?':
                    return literalAtoms >= 2;
                case '{':
                    return literalAtoms >= (MayRepeatZeroTimes(pattern, i) ? 2 : 1);
                case '+':
                    return literalAtoms >= 1;
                case var other when StoppingMetacharacters.Contains(other, StringComparison.Ordinal):
                    return false;
                default:
                    literalAtoms++;
                    break;
            }
        }

        return literalAtoms > 0;
    }

    private static bool IsNegatedClass(string pattern, int open) =>
        open + 1 < pattern.Length && pattern[open + 1] == '^';

    private static bool ContainsNamedClass(string pattern, int open) =>
        pattern.AsSpan(open + 1).StartsWith("[:", StringComparison.Ordinal);

    private static bool MayRepeatZeroTimes(string pattern, int brace) =>
        brace + 1 < pattern.Length && pattern[brace + 1] is '0' or ',';
}
