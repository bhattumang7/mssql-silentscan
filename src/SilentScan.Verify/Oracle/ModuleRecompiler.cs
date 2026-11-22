using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace SilentScan.Verify.Oracle;

[VerifierSupport]
internal static partial class ModuleRecompiler
{
    [GeneratedRegex(@"\bCREATE\b", RegexOptions.IgnoreCase)]
    private static partial Regex CreateKeyword();

    public static async Task<int> RecompileAsync(
        SqlConnection connection, SqlTransaction transaction, string qualifiedName, string setStatement, Func<string, string>? rewrite, CancellationToken cancellationToken)
    {
        if (TableAddress.TryParse(qualifiedName) is not { } module)
        {
            return 0;
        }

        string? definition;
        await using (var read = new SqlCommand("SELECT m.definition FROM sys.sql_modules m WHERE m.object_id = OBJECT_ID(@name);", connection, transaction))
        {
            read.Parameters.AddWithValue("@name", module.Table);
            definition = await read.ExecuteScalarAsync(cancellationToken) as string;
        }

        if (definition is null || !CreateKeyword().IsMatch(definition))
        {
            return 0;
        }

        var alter = CreateKeyword().Replace(definition, "ALTER", 1);
        if (rewrite is not null)
        {
            alter = rewrite(alter);
        }

        await using var command = new SqlCommand($"{setStatement} EXEC sp_executesql @sql;", connection, transaction) { CommandTimeout = 120 };
        command.Parameters.AddWithValue("@sql", alter);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return -1;
    }
}
