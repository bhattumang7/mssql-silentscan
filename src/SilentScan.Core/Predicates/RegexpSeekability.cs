namespace SilentScan.Core.Predicates;

internal static class RegexpSeekability
{
    public static bool CannotDeriveRange(string pattern)
    {
        if (pattern.Length < 2 || pattern[0] != '^')
        {
            return true;
        }

        var index = 1;
        var atoms = 0;
        while (index < pattern.Length)
        {
            var before = atoms;
            if (!TryAdvance(pattern, ref index, ref atoms))
            {
                return true;
            }

            var atomAdded = atoms > before;
            if (atoms == 1 && atomAdded && index < pattern.Length && pattern[index] is '?' or '*')
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryAdvance(string pattern, ref int index, ref int atoms)
    {
        switch (pattern[index])
        {
            case '.' or '(' or ')' or '|' or '$':
                return false;
            case '\\':
                if (index + 1 >= pattern.Length || IsUnsupportedEscape(pattern[index + 1]))
                {
                    return false;
                }

                index += 2;
                atoms++;
                return true;
            case '[':
                if (!TrySkipClass(pattern, ref index, atoms))
                {
                    return false;
                }

                atoms++;
                return true;
            case '?' or '*' or '+' or '{':
                index++;
                return true;
            default:
                index++;
                atoms++;
                return true;
        }
    }

    private const string EscapesWithoutALiteralRange = "abdfnprstvwxzABCDEPQSW";

    private static bool IsUnsupportedEscape(char escaped) =>
        EscapesWithoutALiteralRange.Contains(escaped, StringComparison.Ordinal);

    private static bool TrySkipClass(string pattern, ref int index, int precedingAtoms)
    {
        var next = index + 1;
        if (next >= pattern.Length)
        {
            return false;
        }

        if (pattern[next] == '^' && precedingAtoms == 0)
        {
            return false;
        }

        if (pattern[next] == '[' && next + 1 < pattern.Length && pattern[next + 1] == ':')
        {
            return false;
        }

        var close = pattern.IndexOf(']', next + 1);
        index = close < 0 ? pattern.Length : close + 1;
        return true;
    }
}
