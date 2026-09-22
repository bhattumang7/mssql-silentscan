using System.Text;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.TypeInference;

namespace SilentScan.Core.Rules;

public static partial class WriteLossClassifier
{
    static WriteLossClassifier()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static Predicates.WriteLossKind? Classify(SqlType? target, SqlType? source, ScalarExpression? sourceExpression, bool isVariableTarget)
    {
        if (target is null || source is null)
        {
            return null;
        }

        var literal = Unwrap(sourceExpression) as Literal;

        if (IsUnicodeReplacementRisk(target, source, literal))
        {
            return Predicates.WriteLossKind.UnicodeToNonUnicodeReplacement;
        }

        if (NumericNarrowingKind(target, source, literal) is { } numericKind)
        {
            return numericKind;
        }

        if (IsTemporalPrecisionLossRisk(target, source, literal))
        {
            return Predicates.WriteLossKind.TemporalPrecisionLoss;
        }

        if (IsTemporalOffsetDroppedRisk(target, source))
        {
            return Predicates.WriteLossKind.TemporalOffsetDropped;
        }

        if (IsTemporalScaleNarrowingRisk(target, source))
        {
            return Predicates.WriteLossKind.TemporalScaleNarrowing;
        }

        if (isVariableTarget && IsLengthTruncationRisk(target, source))
        {
            return Predicates.WriteLossKind.LengthTruncation;
        }

        return null;
    }

    private static bool IsUnicodeReplacementRisk(SqlType target, SqlType source, Literal? literal) =>
        source.IsUnicodeString && target.IsNonUnicodeString && target.Collation is not { IsUtf8: true } && !IsRepresentableInTargetCodePage(target, literal);

    private static Predicates.WriteLossKind? NumericNarrowingKind(SqlType target, SqlType source, Literal? literal)
    {
        if (NumericFamilyNarrowing.Classify(target, source) is not { } result)
        {
            return null;
        }

        if (result.TargetIsExact && IsWithinScaleLiteral(literal, result.TargetScale))
        {
            return null;
        }

        return result.Kind == NumericFamilyNarrowing.Kind.ApproximateToExactTruncation
            ? Predicates.WriteLossKind.ApproximateToExactTruncation
            : Predicates.WriteLossKind.NumericScaleNarrowing;
    }

    private static bool IsTemporalPrecisionLossRisk(SqlType target, SqlType source, Literal? literal) =>
        target.Category == SqlTypeCategory.Date && (IsWiderTemporal(source.Category) || source.IsStringFamily) && !IsDateOnlyLiteral(literal);

    public static bool IsTemporalOffsetDroppedRisk(SqlType target, SqlType source) =>
        source.Category == SqlTypeCategory.DateTimeOffset
        && target.Category is SqlTypeCategory.DateTime2 or SqlTypeCategory.DateTime or SqlTypeCategory.SmallDateTime
            or SqlTypeCategory.Date or SqlTypeCategory.Time;

    public static bool IsTemporalScaleNarrowingRisk(SqlType target, SqlType source) =>
        target.IsFractionalSecondsFamily && source.IsFractionalSecondsFamily
        && target.Scale is { } targetScale && source.Scale is { } sourceScale && targetScale < sourceScale;

    private static bool IsLengthTruncationRisk(SqlType target, SqlType source) =>
        (target.IsStringFamily && source.IsStringFamily || target.IsBinaryFamily && source.IsBinaryFamily)
        && !target.IsMax && !source.IsMax
        && target.Length is { } targetLength && source.Length is { } sourceLength && targetLength < sourceLength;

    private static ScalarExpression? Unwrap(ScalarExpression? expression) => expression switch
    {
        ParenthesisExpression paren => Unwrap(paren.Expression),
        UnaryExpression unary => Unwrap(unary.Expression),
        _ => expression,
    };

    private static bool IsWiderTemporal(SqlTypeCategory category) =>
        category is SqlTypeCategory.DateTime or SqlTypeCategory.DateTime2 or SqlTypeCategory.SmallDateTime or SqlTypeCategory.DateTimeOffset;

    private static bool IsRepresentableInTargetCodePage(SqlType target, Literal? literal)
    {
        if (literal is not StringLiteral stringLiteral)
        {
            return false;
        }

        if (stringLiteral.Value.All(c => c <= 127))
        {
            return true;
        }

        if (CollationCodePageCatalog.TryGetCodePage(target.Collation?.Name) is not { } codePage)
        {
            return false;
        }

        var encoding = Encoding.GetEncoding(codePage);
        var value = stringLiteral.Value;
        return encoding.GetString(encoding.GetBytes(value)) == value;
    }

    private static bool IsWithinScaleLiteral(Literal? literal, int targetScale)
    {
        if (literal is IntegerLiteral)
        {
            return true;
        }

        var text = literal switch
        {
            NumericLiteral n => n.Value,
            RealLiteral r => r.Value,
            _ => null,
        };

        if (text is null || text.Contains('e', StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var dot = text.IndexOf('.', StringComparison.Ordinal);
        if (dot < 0)
        {
            return true;
        }

        var fractional = text[(dot + 1)..];
        return fractional.Length <= targetScale || fractional[targetScale..].All(c => c == '0');
    }

    [System.Text.RegularExpressions.GeneratedRegex(
        @"[T ]00:00(:00)?(\.0+)?$", System.Text.RegularExpressions.RegexOptions.None, matchTimeoutMilliseconds: 100)]
    private static partial System.Text.RegularExpressions.Regex MidnightTimeOfDayRegex();

    private static bool IsDateOnlyLiteral(Literal? literal)
    {
        if (literal is not StringLiteral stringLiteral)
        {
            return false;
        }

        var value = stringLiteral.Value;
        if (!value.Contains(':', StringComparison.Ordinal) && !value.Contains('T', StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return MidnightTimeOfDayRegex().IsMatch(value);
    }
}
