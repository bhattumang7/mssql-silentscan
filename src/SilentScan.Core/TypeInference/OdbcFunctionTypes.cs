using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SilentScan.Core.TypeInference;

public static class OdbcFunctionTypes
{
    private static readonly Dictionary<string, SqlType> Fixed = new Dictionary<string, SqlType>(StringComparer.OrdinalIgnoreCase)
    {
        ["CURDATE"] = new(SqlTypeCategory.VarChar, Length: 10),
        ["CURRENT_DATE"] = new(SqlTypeCategory.VarChar, Length: 10),
        ["CURTIME"] = new(SqlTypeCategory.VarChar, Length: 30),
        ["CURRENT_TIME"] = new(SqlTypeCategory.VarChar, Length: 30),
        ["CURRENT_TIMESTAMP"] = new(SqlTypeCategory.VarChar, Length: 30),
        ["NOW"] = new(SqlTypeCategory.DateTime),
        ["TIMESTAMPADD"] = new(SqlTypeCategory.DateTime),
        ["DAYNAME"] = new(SqlTypeCategory.NVarChar, Length: 30),
        ["MONTHNAME"] = new(SqlTypeCategory.NVarChar, Length: 30),
        ["USER"] = new(SqlTypeCategory.NVarChar, Length: 128),
        ["DATABASE"] = new(SqlTypeCategory.NVarChar, Length: 128),
        ["DAYOFMONTH"] = new(SqlTypeCategory.Int),
        ["DAYOFWEEK"] = new(SqlTypeCategory.Int),
        ["DAYOFYEAR"] = new(SqlTypeCategory.Int),
        ["HOUR"] = new(SqlTypeCategory.Int),
        ["MINUTE"] = new(SqlTypeCategory.Int),
        ["MONTH"] = new(SqlTypeCategory.Int),
        ["QUARTER"] = new(SqlTypeCategory.Int),
        ["SECOND"] = new(SqlTypeCategory.Int),
        ["WEEK"] = new(SqlTypeCategory.Int),
        ["YEAR"] = new(SqlTypeCategory.Int),
        ["TIMESTAMPDIFF"] = new(SqlTypeCategory.Int),
        ["LENGTH"] = new(SqlTypeCategory.Int),
        ["OCTET_LENGTH"] = new(SqlTypeCategory.Int),
        ["BIT_LENGTH"] = new(SqlTypeCategory.Int),
        ["LOCATE"] = new(SqlTypeCategory.Int),
        ["ASCII"] = new(SqlTypeCategory.Int),
        ["DIFFERENCE"] = new(SqlTypeCategory.Int),
        ["SQRT"] = new(SqlTypeCategory.Float),
        ["PI"] = new(SqlTypeCategory.Float),
        ["RAND"] = new(SqlTypeCategory.Float),
        ["LOG"] = new(SqlTypeCategory.Float),
        ["EXP"] = new(SqlTypeCategory.Float),
        ["SIN"] = new(SqlTypeCategory.Float),
    };

    private static readonly HashSet<string> ValueOfFirstArgument = new(StringComparer.OrdinalIgnoreCase)
    {
        "UCASE", "LCASE", "LTRIM", "RTRIM", "IFNULL",
    };

    private static readonly Dictionary<string, string> BuiltinEquivalents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["LEFT"] = "LEFT",
        ["RIGHT"] = "RIGHT",
        ["SUBSTRING"] = "SUBSTRING",
        ["REPLACE"] = "REPLACE",
        ["CONCAT"] = "CONCAT",
        ["SPACE"] = "SPACE",
        ["CHAR"] = "CHAR",
        ["SOUNDEX"] = "SOUNDEX",
        ["REPEAT"] = "REPLICATE",
    };

    public static SqlType? Resolve(OdbcFunctionCall call, Func<ScalarExpression, SqlType?> resolveArgument, Func<string, IList<ScalarExpression>, SqlType?> resolveBuiltin)
    {
        var name = call.Name?.Value;
        if (name is null)
        {
            return null;
        }

        if (Fixed.TryGetValue(name, out var fixedType))
        {
            return fixedType;
        }

        if (ValueOfFirstArgument.Contains(name))
        {
            return call.Parameters.Count > 0 ? resolveArgument(call.Parameters[0]) : null;
        }

        return BuiltinEquivalents.TryGetValue(name, out var builtin) ? resolveBuiltin(builtin, call.Parameters) : null;
    }
}
