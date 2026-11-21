using System.Text.RegularExpressions;

namespace SilentScan.Core.Rules;

public static partial class TemporalBoundaryClassifier
{
    [GeneratedRegex(@"(\d{1,2}):(\d{2})(?::(\d{2}))?")]
    private static partial Regex TimeOfDayPattern { get; }

    public static bool HasInsufficientFractionalPrecision(int columnScale, string upperBoundLiteralText, out int literalFractionalDigits)
    {
        literalFractionalDigits = CountFractionalDigits(upperBoundLiteralText);
        return literalFractionalDigits < columnScale && IsEndOfPeriodShape(upperBoundLiteralText);
    }

    private static bool IsEndOfPeriodShape(string text)
    {
        var match = TimeOfDayPattern.Match(text);
        if (!match.Success)
        {
            return true;
        }

        return match.Groups[1].Value == "23"
            && match.Groups[2].Value == "59"
            && (!match.Groups[3].Success || match.Groups[3].Value == "59");
    }

    private static int CountFractionalDigits(string text)
    {
        var dotIndex = text.LastIndexOf('.');
        if (dotIndex < 0)
        {
            return 0;
        }

        var digits = 0;
        for (var i = dotIndex + 1; i < text.Length && char.IsDigit(text[i]); i++)
        {
            digits++;
        }

        return digits;
    }
}
