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

    public static Predicates.WriteLossKind? Classify(
        SqlType? target, SqlType? source, ScalarExpression? sourceExpression, bool isVariableTarget,
        Func<ScalarExpression, SqlType?>? operandType = null)
    {
        if (target is null || source is null)
        {
            return null;
        }

        var literal = Unwrap(sourceExpression) as Literal;

        if (IsUnicodeReplacementRisk(target, source, literal) && !IsProvablyRepresentable(target, sourceExpression, operandType))
        {
            return Predicates.WriteLossKind.UnicodeToNonUnicodeReplacement;
        }

        if (NumericNarrowingKind(target, source, literal) is { } numericKind)
        {
            return numericKind;
        }

        if (IsExactToApproximatePrecisionRisk(target, source, literal))
        {
            return Predicates.WriteLossKind.ExactToApproximatePrecisionLoss;
        }

        if (IsTemporalPrecisionLossRisk(target, source, literal) && !IsProvablyMidnight(sourceExpression, operandType))
        {
            return Predicates.WriteLossKind.TemporalPrecisionLoss;
        }

        if (IsTemporalOffsetDroppedRisk(target, source))
        {
            return Predicates.WriteLossKind.TemporalOffsetDropped;
        }

        if (IsTemporalScaleNarrowingRisk(target, source) || LiteralLosesFractionalDigits(target, source, literal))
        {
            return Predicates.WriteLossKind.TemporalScaleNarrowing;
        }

        if (IsSmallDateTimeRoundingRisk(target, source, literal) && !IsProvablyMidnight(sourceExpression, operandType))
        {
            return Predicates.WriteLossKind.SmallDateTimeRounding;
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

    private const int RealDigitsKept = 7;

    private const int FloatDigitsKept = 15;

    private const int LargestRealMantissaBits = 24;

    private static bool IsExactToApproximatePrecisionRisk(SqlType target, SqlType source, Literal? literal) =>
        ApproximateDigitsKept(target) is { } kept
        && ExactDigits(source) > kept
        && (literal is null || SignificantDigits(literal) > kept);

    private static int? ApproximateDigitsKept(SqlType target) => target.Category switch
    {
        SqlTypeCategory.Real => RealDigitsKept,
        SqlTypeCategory.Float => target.Precision is { } bits && bits <= LargestRealMantissaBits ? RealDigitsKept : FloatDigitsKept,
        _ => null,
    };

    private static int ExactDigits(SqlType source) => source.Category switch
    {
        SqlTypeCategory.TinyInt => 3,
        SqlTypeCategory.SmallInt => 5,
        SqlTypeCategory.Int or SqlTypeCategory.SmallMoney => 10,
        SqlTypeCategory.BigInt or SqlTypeCategory.Money => 19,
        SqlTypeCategory.Decimal => source.Precision ?? 18,
        _ => 0,
    };

    private static int SignificantDigits(Literal literal) =>
        string.Concat(literal.Value.Where(char.IsAsciiDigit)).Trim('0').Length;

    private static bool IsTemporalPrecisionLossRisk(SqlType target, SqlType source, Literal? literal) =>
        target.Category == SqlTypeCategory.Date && (IsWiderTemporal(source.Category) || source.IsStringFamily) && !IsDateOnlyLiteral(literal);

    public static bool IsTemporalOffsetDroppedRisk(SqlType target, SqlType source) =>
        source.Category == SqlTypeCategory.DateTimeOffset
        && target.Category is SqlTypeCategory.DateTime2 or SqlTypeCategory.DateTime or SqlTypeCategory.SmallDateTime
            or SqlTypeCategory.Date or SqlTypeCategory.Time;

    private static bool LiteralLosesFractionalDigits(SqlType target, SqlType source, Literal? literal) =>
        source.IsStringFamily
        && target.IsFractionalSecondsFamily
        && target.Scale is { } scale
        && literal is StringLiteral stringLiteral
        && FractionalDigitsRegex().Match(stringLiteral.Value) is { Success: true } match
        && match.Groups[1].Value.AsSpan(Math.Min(scale, match.Groups[1].Length)).IndexOfAnyExcept('0') >= 0;

    public static bool IsTemporalScaleNarrowingRisk(SqlType target, SqlType source) =>
        target.IsFractionalSecondsFamily && source.IsFractionalSecondsFamily
        && target.Scale is { } targetScale && source.Scale is { } sourceScale && targetScale < sourceScale
        || IsDateTimeGridLoss(target, source);

    private static bool IsDateTimeGridLoss(SqlType target, SqlType source) =>
        target.Category == SqlTypeCategory.DateTime
            && source.Category is SqlTypeCategory.DateTime2 or SqlTypeCategory.DateTimeOffset
            && source.Scale is >= 3
        || source.Category == SqlTypeCategory.DateTime
            && target.Category is SqlTypeCategory.DateTime2 or SqlTypeCategory.DateTimeOffset
            && target.Scale is < 7;

    private static bool IsSmallDateTimeRoundingRisk(SqlType target, SqlType source, Literal? literal) =>
        target.Category == SqlTypeCategory.SmallDateTime
        && (source.Category is SqlTypeCategory.DateTime or SqlTypeCategory.DateTime2
            || source.IsStringFamily && literal is StringLiteral stringLiteral && SecondsComponentRegex().IsMatch(stringLiteral.Value));

    private static bool IsLengthTruncationRisk(SqlType target, SqlType source)
    {
        if (target.IsMax || target.Length is not { } targetLength)
        {
            return false;
        }

        if (target.IsStringFamily && source.IsStringFamily || target.IsBinaryFamily && source.IsBinaryFamily)
        {
            return source.IsMax || source.Length > targetLength;
        }

        if (!target.IsStringFamily)
        {
            return false;
        }

        if (source.IsBinaryFamily)
        {
            return source.IsMax || source.Length > (target.IsUnicodeString ? targetLength * 2 : targetLength);
        }

        return TemporalTextLength(source) > targetLength;
    }

    private static int? TemporalTextLength(SqlType source) => source.Category switch
    {
        SqlTypeCategory.Date => DateTextLength,
        SqlTypeCategory.Time => TimeTextLength + FractionTextLength(source),
        SqlTypeCategory.DateTime or SqlTypeCategory.SmallDateTime => DateTimeTextLength,
        SqlTypeCategory.DateTime2 => DateTimeTextLength + FractionTextLength(source),
        SqlTypeCategory.DateTimeOffset => DateTimeTextLength + FractionTextLength(source) + OffsetTextLength,
        _ => null,
    };

    private static int FractionTextLength(SqlType source) =>
        (source.Scale ?? DefaultFractionalScale) is var scale && scale > 0 ? scale + 1 : 0;

    private const int DateTextLength = 10;

    private const int TimeTextLength = 8;

    private const int DateTimeTextLength = 19;

    private const int OffsetTextLength = 7;

    private const int DefaultFractionalScale = 7;

    private static ScalarExpression? Unwrap(ScalarExpression? expression) => expression switch
    {
        ParenthesisExpression paren => Unwrap(paren.Expression),
        UnaryExpression unary => Unwrap(unary.Expression),
        _ => expression,
    };

    private static bool IsWiderTemporal(SqlTypeCategory category) =>
        category is SqlTypeCategory.DateTime or SqlTypeCategory.DateTime2 or SqlTypeCategory.SmallDateTime or SqlTypeCategory.DateTimeOffset;

    private static bool IsProvablyRepresentable(SqlType target, ScalarExpression? expression, Func<ScalarExpression, SqlType?>? operandType)
    {
        switch (expression)
        {
            case null:
                return false;
            case ParenthesisExpression paren:
                return IsProvablyRepresentable(target, paren.Expression, operandType);
            case StringLiteral stringLiteral:
                return IsRepresentableInTargetCodePage(target, stringLiteral);
            case NullLiteral:
                return true;
            case FunctionCall { Parameters.Count: 2 } nullHandling
                when nullHandling.CallTarget is null && nullHandling.FunctionName.Value.Equals("ISNULL", StringComparison.OrdinalIgnoreCase):
                return nullHandling.Parameters.All(parameter => IsProvablyRepresentable(target, parameter, operandType));
            case CoalesceExpression coalesce:
                return coalesce.Expressions.All(item => IsProvablyRepresentable(target, item, operandType));
            case BinaryExpression { BinaryExpressionType: BinaryExpressionType.Add } add:
                return IsProvablyRepresentable(target, add.FirstExpression, operandType)
                    && IsProvablyRepresentable(target, add.SecondExpression, operandType);
            case FunctionCall { Parameters.Count: >= 1 and <= 2 } call
                when call.FunctionName.Value.Equals("QUOTENAME", StringComparison.OrdinalIgnoreCase) && call.CallTarget is null:
                return IsProvablyRepresentable(target, call.Parameters[0], operandType)
                    && (call.Parameters.Count == 1 || call.Parameters[1] is StringLiteral { Value: { Length: 1 } quote } && quote[0] <= 127);
            case CastCall { Parameter: { } castOperand } cast when IsStringCast(cast.DataType):
                return IsAsciiOnlyTextRendering(castOperand, operandType);
            case ConvertCall { Parameter: { } convertOperand } convert when IsStringCast(convert.DataType):
                return IsAsciiOnlyTextRendering(convertOperand, operandType);
            case ColumnReferenceExpression or VariableReference when operandType?.Invoke(expression) is { IsNonUnicodeString: true } leaf:
                return SameCodePage(leaf, target);
            default:
                return false;
        }
    }

    private static bool IsStringCast(DataTypeReference dataType) =>
        dataType is SqlDataTypeReference { SqlDataTypeOption: SqlDataTypeOption.NVarChar or SqlDataTypeOption.NChar or SqlDataTypeOption.VarChar or SqlDataTypeOption.Char };

    private static bool IsAsciiOnlyTextRendering(ScalarExpression operand, Func<ScalarExpression, SqlType?>? operandType) =>
        Unwrap(operand) is { } inner
        && operandType?.Invoke(inner) is { } type
        && (type.IsNumericFamily || type.Category is SqlTypeCategory.Bit or SqlTypeCategory.UniqueIdentifier);

    private static bool SameCodePage(SqlType leaf, SqlType target)
    {
        var leafName = leaf.Collation?.Name;
        var targetName = target.Collation?.Name;
        if (string.Equals(leafName, targetName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return CollationCodePageCatalog.TryGetCodePage(leafName) is { } leafPage
            && CollationCodePageCatalog.TryGetCodePage(targetName) is { } targetPage
            && leafPage == targetPage;
    }

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

    [System.Text.RegularExpressions.GeneratedRegex(
        @"\d{1,2}:\d{2}:\d{2}\.(\d+)", System.Text.RegularExpressions.RegexOptions.None, matchTimeoutMilliseconds: 100)]
    private static partial System.Text.RegularExpressions.Regex FractionalDigitsRegex();

    [System.Text.RegularExpressions.GeneratedRegex(
        @"[T ]\d{1,2}:\d{2}:(?:0[1-9]|[1-9]\d|00\.\d*[1-9])", System.Text.RegularExpressions.RegexOptions.None, matchTimeoutMilliseconds: 100)]
    private static partial System.Text.RegularExpressions.Regex SecondsComponentRegex();

    private static readonly HashSet<string> WholeDateDatePartNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "year", "yy", "yyyy", "quarter", "qq", "q", "month", "mm", "m", "dayofyear", "dy", "y",
        "day", "dd", "d", "week", "wk", "ww", "weekday", "dw", "w",
    };

    private static bool IsProvablyMidnight(ScalarExpression? expression, Func<ScalarExpression, SqlType?>? operandType)
    {
        switch (expression)
        {
            case ParenthesisExpression paren:
                return IsProvablyMidnight(paren.Expression, operandType);
            case CastCall { DataType: SqlDataTypeReference cast } castCall when IsDateTimeTarget(cast):
                return IsMidnightConversionSource(castCall.Parameter, operandType);
            case ConvertCall { DataType: SqlDataTypeReference convert } convertCall when IsDateTimeTarget(convert):
                return IsMidnightConversionSource(convertCall.Parameter, operandType);
            case FunctionCall { Parameters.Count: 3, CallTarget: null } call
                when call.FunctionName.Value.Equals("DATEADD", StringComparison.OrdinalIgnoreCase)
                    && DatePartName(call.Parameters[0]) is { } datePart
                    && WholeDateDatePartNames.Contains(datePart):
                return IsMidnightDateBase(call.Parameters[2], operandType);
            default:
                return false;
        }
    }

    private static string? DatePartName(ScalarExpression expression) =>
        expression.ScriptTokenStream is { } tokens && expression.FirstTokenIndex >= 0 && expression.FirstTokenIndex == expression.LastTokenIndex
            ? tokens[expression.FirstTokenIndex].Text
            : null;

    private static bool IsDateTimeTarget(SqlDataTypeReference dataType) =>
        dataType.SqlDataTypeOption is SqlDataTypeOption.DateTime or SqlDataTypeOption.DateTime2 or SqlDataTypeOption.SmallDateTime;

    private static bool IsMidnightDateBase(ScalarExpression expression, Func<ScalarExpression, SqlType?>? operandType)
    {
        return Unwrap(expression) is { } inner
            && (inner is IntegerLiteral
                || IsMidnightConversionSource(inner, operandType)
                || IsProvablyMidnight(inner, operandType));
    }

    private static bool IsMidnightConversionSource(ScalarExpression expression, Func<ScalarExpression, SqlType?>? operandType)
    {
        if (Unwrap(expression) is not { } inner)
        {
            return false;
        }

        switch (inner)
        {
            case StringLiteral:
                return IsDateOnlyLiteral(inner as Literal);
            case CastCall { DataType: SqlDataTypeReference cast } castCall when IsStringTarget(cast):
                return IsIntegerOperand(castCall.Parameter, operandType);
            case ConvertCall { DataType: SqlDataTypeReference convert } convertCall when IsStringTarget(convert):
                return IsIntegerOperand(convertCall.Parameter, operandType);
            default:
                return operandType?.Invoke(inner) is { Category: SqlTypeCategory.Date } || IsProvablyMidnight(inner, operandType);
        }
    }

    private static bool IsStringTarget(SqlDataTypeReference dataType) =>
        dataType.SqlDataTypeOption is SqlDataTypeOption.Char or SqlDataTypeOption.VarChar or SqlDataTypeOption.NChar or SqlDataTypeOption.NVarChar;

    private static bool IsIntegerOperand(ScalarExpression expression, Func<ScalarExpression, SqlType?>? operandType) =>
        Unwrap(expression) is { } inner
        && (inner is IntegerLiteral
            || operandType?.Invoke(inner) is { Category: SqlTypeCategory.TinyInt or SqlTypeCategory.SmallInt or SqlTypeCategory.Int or SqlTypeCategory.BigInt });

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
