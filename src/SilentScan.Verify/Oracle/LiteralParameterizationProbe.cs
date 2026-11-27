using System.Text;
using System.Text.RegularExpressions;

namespace SilentScan.Verify.Oracle;

[VerifierSupport]
internal static partial class LiteralParameterizationProbe
{
    private const string Sample = "7";

    private const string OtherSample = "8";

    public static Perturbation Describe(string? spliced)
    {
        if (string.IsNullOrEmpty(spliced) || !PlaceholderToken().IsMatch(spliced))
        {
            return Perturbation.NotApplicable("The finding does not carry a spliced value the probe can change.");
        }

        var shape = PlaceholderToken().Replace(spliced, Sample);
        return new Perturbation(
            [],
            Difference.PlanTextChanged,
            static (_, _, _) => Task.FromResult(0),
            static (_, _, _) => Task.FromResult(0),
            static _ => false,
            baselineStatement: statement => Render(statement, Sample) is { } inner && inner.Contains(shape, StringComparison.Ordinal) ? inner : null,
            options: new PerturbationOptions(AppliedStatement: statement => Render(statement, OtherSample)));
    }

    private static string? Render(string statement, string sample)
    {
        var start = statement.IndexOf("= N'", StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        var text = new StringBuilder();
        var position = start + 2;
        while (position < statement.Length)
        {
            if (statement.AsSpan(position).StartsWith("N'", StringComparison.Ordinal))
            {
                position = AppendLiteral(statement, position + 2, text);
            }
            else
            {
                position = EndOfTerm(statement, position);
                text.Append(sample);
            }

            position = SkipSpaces(statement, position);
            if (position >= statement.Length || statement[position] != '+')
            {
                return text.ToString();
            }

            position = SkipSpaces(statement, position + 1);
        }

        return null;
    }

    private static int AppendLiteral(string statement, int position, StringBuilder text)
    {
        while (position < statement.Length)
        {
            if (statement[position] != '\'')
            {
                text.Append(statement[position++]);
            }
            else if (position + 1 < statement.Length && statement[position + 1] == '\'')
            {
                text.Append('\'');
                position += 2;
            }
            else
            {
                return position + 1;
            }
        }

        return position;
    }

    private static int EndOfTerm(string statement, int position)
    {
        var depth = 0;
        while (position < statement.Length && (depth > 0 || statement[position] is not ('+' or ';')))
        {
            depth += statement[position] switch { '(' => 1, ')' => -1, _ => 0 };
            position++;
        }

        return position;
    }

    private static int SkipSpaces(string statement, int position)
    {
        while (position < statement.Length && char.IsWhiteSpace(statement[position]))
        {
            position++;
        }

        return position;
    }

    [GeneratedRegex("__silentscan_sym_L\\d+C\\d+__")]
    private static partial Regex PlaceholderToken();
}
