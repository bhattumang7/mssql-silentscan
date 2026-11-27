using System.Globalization;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SilentScan.Core.TypeInference;

public static class LiteralTypeResolver
{
    private const int MaxIntLiteralLength = 11;

    public static SqlType? Resolve(Literal literal) => literal switch
    {
        StringLiteral { IsNational: true } s => new SqlType(SqlTypeCategory.NVarChar, Length: EmptyStringAwareLength(s.Value), Collation: ExplicitCollation(s)),

        StringLiteral s => new SqlType(SqlTypeCategory.VarChar, Length: EmptyStringAwareLength(s.Value), Collation: ExplicitCollation(s)),

        IntegerLiteral => new SqlType(SqlTypeCategory.Int),

        NumericLiteral n => ResolveNumeric(n),

        RealLiteral => new SqlType(SqlTypeCategory.Float, Precision: 53),

        MoneyLiteral => new SqlType(SqlTypeCategory.Money),

        BinaryLiteral b => new SqlType(SqlTypeCategory.VarBinary, Length: Math.Max(1, (b.Value.Length - 1) / 2)),

        _ => null,
    };

    private static int EmptyStringAwareLength(string value) => value.Length == 0 ? 1 : value.Length;

    private static Collation? ExplicitCollation(Literal literal) =>
        literal.Collation is { Value: { } name } ? new Collation(name, CollationSource.ExplicitCollateClause) : null;

    private static SqlType ResolveNumeric(NumericLiteral literal)
    {
        var value = literal.Value;
        var dotIndex = value.IndexOf('.', StringComparison.Ordinal);
        if (dotIndex < 0)
        {
            return value.Length <= MaxIntLiteralLength && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _)
                ? new SqlType(SqlTypeCategory.Int)
                : new SqlType(SqlTypeCategory.Decimal, Precision: Math.Max(1, value.TrimStart('0').Length), Scale: 0);
        }

        var integerDigits = value[..dotIndex].TrimStart('0').Length;
        var fractionalDigits = value.Length - dotIndex - 1;
        return new SqlType(SqlTypeCategory.Decimal, Precision: Math.Max(1, integerDigits + fractionalDigits), Scale: fractionalDigits);
    }
}
