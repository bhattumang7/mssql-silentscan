using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Catalog;

namespace SilentScan.Core.Rules;

/// <summary>
/// T-SQL literal typing rules (CLAUDE.md): N'x' = nvarchar, 'x' = varchar, integer literal =
/// int, 1.5 = numeric(p,s), date literals stay strings (varchar) until compared.
/// </summary>
public static class LiteralTypeResolver
{
    public static SqlType? Resolve(Literal literal) => literal switch
    {
        StringLiteral { IsNational: true } s => new SqlType(SqlTypeCategory.NVarChar, Length: s.Value.Length),

        // Date/time literals are untyped strings until compared against a typed column -
        // that comparison-time typing is a Pass 3 concern, not this pass's.
        StringLiteral s => new SqlType(SqlTypeCategory.VarChar, Length: s.Value.Length),

        IntegerLiteral => new SqlType(SqlTypeCategory.Int),

        NumericLiteral n => ResolveNumeric(n),

        MoneyLiteral => new SqlType(SqlTypeCategory.Money),

        // Value includes the "0x" prefix (e.g. "0x1A2B"); two hex digits per byte.
        BinaryLiteral b => new SqlType(SqlTypeCategory.Binary, Length: (b.Value.Length - 2) / 2),

        _ => null,
    };

    private static SqlType ResolveNumeric(NumericLiteral literal)
    {
        var value = literal.Value;
        var dotIndex = value.IndexOf('.', StringComparison.Ordinal);
        if (dotIndex < 0)
        {
            return new SqlType(SqlTypeCategory.Decimal, Precision: value.Length, Scale: 0);
        }

        var integerDigits = dotIndex;
        var fractionalDigits = value.Length - dotIndex - 1;
        return new SqlType(SqlTypeCategory.Decimal, Precision: integerDigits + fractionalDigits, Scale: fractionalDigits);
    }
}
