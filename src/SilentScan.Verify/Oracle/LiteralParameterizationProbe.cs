using System.Globalization;

namespace SilentScan.Verify.Oracle;

[VerifierSupport]
internal static class LiteralParameterizationProbe
{
    public static Perturbation Describe(string? literal)
    {
        if (Alternative(literal) is not { } alternative)
        {
            return Perturbation.NotApplicable("The finding does not carry a literal the probe can change to another value.");
        }

        return new Perturbation(
            [new TouchedExpression(literal!)],
            Difference.PlanTextChanged,
            static (_, _, _) => Task.FromResult(0),
            static (_, _, _) => Task.FromResult(0),
            static _ => false,
            baselineStatement: statement => statement.Replace(literal!, alternative, StringComparison.Ordinal));
    }

    private static string? Alternative(string? literal)
    {
        if (string.IsNullOrEmpty(literal))
        {
            return null;
        }

        if (long.TryParse(literal, NumberStyles.None, CultureInfo.InvariantCulture, out var whole))
        {
            return (whole + 1).ToString(CultureInfo.InvariantCulture);
        }

        if (decimal.TryParse(literal, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out _))
        {
            return literal + "1";
        }

        var quote = literal.LastIndexOf('\'');
        return quote > 0 && quote == literal.Length - 1
            ? literal[..quote] + "x'"
            : null;
    }
}
