using System.Text;
using System.Text.RegularExpressions;

namespace SilentScan.Verify.Oracle;

[VerifierSupport]
internal static class StringBuiltinWidening
{
    private const string Widen = "CONVERT(VARCHAR(MAX), 0x)";

    public static string? Rewrite(string statement, string functionName)
    {
        var call = new Regex(@"\b" + Regex.Escape(functionName) + @"\s*\(", RegexOptions.IgnoreCase);
        var matches = call.Matches(statement);
        if (matches.Count != 1)
        {
            return null;
        }

        var open = matches[0];
        if (functionName.Equals("SPACE", StringComparison.OrdinalIgnoreCase))
        {
            return statement[..open.Index] + "REPLICATE(CONVERT(VARCHAR(MAX), 0x20), " + statement[(open.Index + open.Length)..];
        }

        var comma = FirstTopLevelComma(statement, open.Index + open.Length);
        if (comma < 0)
        {
            return null;
        }

        var start = open.Index + open.Length;
        var argument = statement[start..comma];
        return new StringBuilder()
            .Append(statement, 0, start)
            .Append("((").Append(argument).Append(") + ").Append(Widen).Append(')')
            .Append(statement, comma, statement.Length - comma)
            .ToString();
    }

    private static int FirstTopLevelComma(string text, int from)
    {
        var depth = 0;
        var inString = false;
        for (var i = from; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                inString = c != '\'';
                continue;
            }

            switch (c)
            {
                case '\'':
                    inString = true;
                    break;
                case '(':
                    depth++;
                    break;
                case ')' when depth == 0:
                    return -1;
                case ')':
                    depth--;
                    break;
                case ',' when depth == 0:
                    return i;
            }
        }

        return -1;
    }
}
