using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.TypeInference;

namespace SilentScan.Core.Lineage;

internal static class GlobalFunctionRowsets
{
    public static List<ResolvedColumn> OpenJsonDefaultColumns() =>
    [
        new("key", new ColumnProvenance.Declared(new SqlType(SqlTypeCategory.NVarChar, Length: 4000))),
        new("value", new ColumnProvenance.Declared(new SqlType(SqlTypeCategory.NVarChar, IsMax: true))),
        new("type", new ColumnProvenance.Declared(new SqlType(SqlTypeCategory.TinyInt))),
    ];

    public static ResolvedRelation? TryResolve(GlobalFunctionTableReference reference, Func<ScalarExpression, SqlType?> resolveArgument)
    {
        var name = reference.Name.Value;
        var firstArgument = reference.Parameters.Count > 0
            ? resolveArgument(reference.Parameters[0])
            : null;
        List<ResolvedColumn>? columns = null;
        if (string.Equals(name, "STRING_SPLIT", StringComparison.OrdinalIgnoreCase) && reference.Parameters.Count is 2 or 3)
        {
            columns = [new("value", ProvenanceOf(firstArgument is { IsStringFamily: true } text ? SplitValue(text) : null, "STRING_SPLIT input type"))];
            if (reference.Parameters.Count == 3)
            {
                columns.Add(new("ordinal", new ColumnProvenance.Declared(new SqlType(SqlTypeCategory.BigInt))));
            }
        }
        else if (string.Equals(name, "GENERATE_SERIES", StringComparison.OrdinalIgnoreCase) && reference.Parameters.Count is 2 or 3)
        {
            columns = [new("value", ProvenanceOf(firstArgument, "GENERATE_SERIES start type"))];
        }

        return columns is null ? null : new ResolvedRelation(QualifiedName: null, columns);
    }

    private static SqlType SplitValue(SqlType input) => input with { Category = input.Category switch { SqlTypeCategory.Char => SqlTypeCategory.VarChar, SqlTypeCategory.NChar => SqlTypeCategory.NVarChar, var other => other } };

    private static ColumnProvenance ProvenanceOf(SqlType? type, string reason) =>
        type is null ? new ColumnProvenance.Unknown(reason) : new ColumnProvenance.Declared(type);
}
