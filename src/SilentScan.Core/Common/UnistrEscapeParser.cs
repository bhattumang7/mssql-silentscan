namespace SilentScan.Core.Common;

internal static class UnistrEscapeParser
{
    public readonly record struct UnicodeEscape(int CodePoint, int StartIndex, int EndIndex, string Text);

    public static List<UnicodeEscape> ParseEscapes(string value)
    {
        var escapes = new List<UnicodeEscape>();
        var i = 0;

        while (i < value.Length)
        {
            if (value[i] != '\\')
            {
                i++;
                continue;
            }

            if (i + 1 < value.Length && value[i + 1] == '\\')
            {
                i += 2;
                continue;
            }

            if (i + 1 < value.Length && value[i + 1] == '+'
                && i + 8 <= value.Length
                && TryParseHex(value, i + 2, 6, out var wideCodePoint))
            {
                escapes.Add(new UnicodeEscape(wideCodePoint, i, i + 8, value.Substring(i, 8)));
                i += 8;
                continue;
            }

            if (i + 5 <= value.Length && TryParseHex(value, i + 1, 4, out var codePoint))
            {
                escapes.Add(new UnicodeEscape(codePoint, i, i + 5, value.Substring(i, 5)));
                i += 5;
                continue;
            }

            i++;
        }

        return escapes;
    }

    private static bool TryParseHex(string value, int start, int length, out int result)
    {
        result = 0;
        for (var i = start; i < start + length; i++)
        {
            var c = value[i];
            var digit = c switch
            {
                >= '0' and <= '9' => c - '0',
                >= 'a' and <= 'f' => c - 'a' + 10,
                >= 'A' and <= 'F' => c - 'A' + 10,
                _ => -1,
            };

            if (digit < 0)
            {
                return false;
            }

            result = (result << 4) | digit;
        }

        return true;
    }
}
