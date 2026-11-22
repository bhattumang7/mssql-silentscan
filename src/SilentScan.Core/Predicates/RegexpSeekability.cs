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
            var current = pattern[index];
            var before = atoms;
            switch (current)
            {
                case '.' or '(' or ')' or '|' or '$':
                    return true;
                case '\\':
                    if (index + 1 >= pattern.Length || IsUnsupportedEscape(pattern[index + 1]))
                    {
                        return true;
                    }

                    index += 2;
                    atoms++;
                    break;
                case '[':
                    if (!TrySkipClass(pattern, ref index, atoms))
                    {
                        return true;
                    }

                    atoms++;
                    break;
                case '?' or '*' or '+' or '{':
                    index++;
                    break;
                default:
                    index++;
                    atoms++;
                    break;
            }

            var atomAdded = atoms > before;
            if (atoms == 1 && atomAdded && index < pattern.Length && pattern[index] is '?' or '*')
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsUnsupportedEscape(char escaped) =>
        escaped != 'Z' && char.IsAsciiLetterOrDigit(escaped);

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
