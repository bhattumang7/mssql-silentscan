using System.Globalization;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Catalog;

namespace SilentScan.Core.Parsing;

/// <summary>Resolves a ScriptDOM <see cref="DataTypeReference"/> (as written in DDL) to a <see cref="SqlType"/>.</summary>
public static class SqlTypeReferenceResolver
{
    /// <param name="dataType">The type as written in DDL.</param>
    /// <param name="columnCollation">The COLUMN's own COLLATE clause, if any is present on the declaration.</param>
    public static SqlType? Resolve(DataTypeReference dataType, Identifier? columnCollation)
    {
        if (dataType is not SqlDataTypeReference sqlDataType)
        {
            // User-defined types (ColumnType/user schema types) are out of scope for v1's
            // type-precedence reasoning; callers should treat this as SqlTypeCategory.UserDefined.
            return null;
        }

        var category = SqlDataTypeMapper.Map(sqlDataType.SqlDataTypeOption);
        if (category is null)
        {
            return null;
        }

        var collation = columnCollation is { Value.Length: > 0 } ? new Collation(columnCollation.Value) : null;

        return sqlDataType.SqlDataTypeOption switch
        {
            SqlDataTypeOption.Decimal or SqlDataTypeOption.Numeric => ResolveDecimal(category.Value, sqlDataType),
            _ when IsStringOrBinaryFamily(category.Value) => ResolveStringOrBinary(category.Value, sqlDataType, collation),
            _ => new SqlType(category.Value),
        };
    }

    private static bool IsStringOrBinaryFamily(SqlTypeCategory category) => category is
        SqlTypeCategory.Char or SqlTypeCategory.VarChar or SqlTypeCategory.NChar or SqlTypeCategory.NVarChar
        or SqlTypeCategory.Binary or SqlTypeCategory.VarBinary;

    private static SqlType ResolveStringOrBinary(SqlTypeCategory category, SqlDataTypeReference sqlDataType, Collation? collation)
    {
        var lengthParam = sqlDataType.Parameters.Count > 0 ? sqlDataType.Parameters[0] : null;
        if (lengthParam is MaxLiteral)
        {
            return new SqlType(category, Collation: collation, IsMax: true);
        }

        var length = lengthParam is IntegerLiteral intLiteral ? int.Parse(intLiteral.Value, CultureInfo.InvariantCulture) : (int?)null;
        return new SqlType(category, Length: length, Collation: collation);
    }

    private static SqlType ResolveDecimal(SqlTypeCategory category, SqlDataTypeReference sqlDataType)
    {
        int? precision = null;
        int? scale = null;
        if (sqlDataType.Parameters.Count > 0 && sqlDataType.Parameters[0] is IntegerLiteral p)
        {
            precision = int.Parse(p.Value, CultureInfo.InvariantCulture);
        }

        if (sqlDataType.Parameters.Count > 1 && sqlDataType.Parameters[1] is IntegerLiteral s)
        {
            scale = int.Parse(s.Value, CultureInfo.InvariantCulture);
        }

        return new SqlType(category, Precision: precision, Scale: scale);
    }
}
