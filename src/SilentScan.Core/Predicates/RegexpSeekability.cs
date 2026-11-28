namespace SilentScan.Core.Predicates;

internal static class RegexpSeekability
{
    public static bool RangeHoldsALetter(string pattern)
    {
        for (var index = 1; index < pattern.Length && char.IsAsciiLetterOrDigit(pattern[index]); index++)
        {
            if (index + 1 < pattern.Length && pattern[index + 1] is '?' or '*' or '{')
            {
                return false;
            }

            if (char.IsAsciiLetter(pattern[index]))
            {
                return true;
            }
        }

        return false;
    }

    public static char? LastCaseFlag(string flags)
    {
        var last = flags.LastIndexOfAny(['c', 'i']);
        return last < 0 ? null : flags[last];
    }
}
