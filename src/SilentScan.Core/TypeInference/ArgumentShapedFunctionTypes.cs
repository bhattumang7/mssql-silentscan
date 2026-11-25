namespace SilentScan.Core.TypeInference;

public static class ArgumentShapedFunctionTypes
{
    private enum Shape
    {
        Preserving,
        IntegralDecimal,
        PowerDecimal,
        AngleDecimal,
        DateTimeOffsetScale,
    }

    private static readonly Dictionary<string, Shape> Shapes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ABS"] = Shape.Preserving,
        ["SIGN"] = Shape.Preserving,
        ["ROUND"] = Shape.Preserving,
        ["CEILING"] = Shape.IntegralDecimal,
        ["FLOOR"] = Shape.IntegralDecimal,
        ["POWER"] = Shape.PowerDecimal,
        ["RADIANS"] = Shape.AngleDecimal,
        ["DEGREES"] = Shape.AngleDecimal,
        ["TODATETIMEOFFSET"] = Shape.DateTimeOffsetScale,
        ["SWITCHOFFSET"] = Shape.DateTimeOffsetScale,
    };

    public static bool IsArgumentShaped(string functionName) => Shapes.ContainsKey(functionName);

    public static IReadOnlyCollection<string> FunctionNames => Shapes.Keys;

    public static SqlType? Resolve(string functionName, SqlType? firstArgument)
    {
        if (firstArgument is null || !Shapes.TryGetValue(functionName, out var shape))
        {
            return null;
        }

        return shape == Shape.DateTimeOffsetScale ? ResolveDateTimeOffset(firstArgument) : ResolveNumeric(shape, firstArgument);
    }

    private static SqlType? ResolveDateTimeOffset(SqlType argument) => argument.Category switch
    {
        SqlTypeCategory.DateTime => new SqlType(SqlTypeCategory.DateTimeOffset, Scale: 3),
        SqlTypeCategory.SmallDateTime => new SqlType(SqlTypeCategory.DateTimeOffset, Scale: 0),
        SqlTypeCategory.DateTime2 or SqlTypeCategory.DateTimeOffset or SqlTypeCategory.Time => new SqlType(SqlTypeCategory.DateTimeOffset, Scale: argument.Scale ?? 7),
        SqlTypeCategory.Date => new SqlType(SqlTypeCategory.DateTimeOffset, Scale: 7),
        _ when argument.IsStringFamily => new SqlType(SqlTypeCategory.DateTimeOffset, Scale: 7),
        _ => null,
    };

    private static SqlType? ResolveNumeric(Shape shape, SqlType argument)
    {
        switch (argument.Category)
        {
            case SqlTypeCategory.TinyInt or SqlTypeCategory.SmallInt or SqlTypeCategory.Int:
                return new SqlType(SqlTypeCategory.Int);

            case SqlTypeCategory.BigInt:
                return new SqlType(SqlTypeCategory.BigInt);

            case SqlTypeCategory.Decimal when argument is { Precision: { } precision, Scale: { } scale }:
                return shape switch
                {
                    Shape.IntegralDecimal => new SqlType(SqlTypeCategory.Decimal, Precision: precision, Scale: 0),
                    Shape.PowerDecimal => new SqlType(SqlTypeCategory.Decimal, Precision: 38, Scale: scale),
                    Shape.AngleDecimal => new SqlType(SqlTypeCategory.Decimal, Precision: 38, Scale: 18),
                    _ => new SqlType(SqlTypeCategory.Decimal, Precision: precision, Scale: scale),
                };

            case SqlTypeCategory.Money or SqlTypeCategory.SmallMoney:
                return new SqlType(SqlTypeCategory.Money);

            case SqlTypeCategory.Float or SqlTypeCategory.Real or SqlTypeCategory.Bit:
                return new SqlType(SqlTypeCategory.Float);

            default:
                return argument.IsStringFamily ? new SqlType(SqlTypeCategory.Float) : null;
        }
    }
}
