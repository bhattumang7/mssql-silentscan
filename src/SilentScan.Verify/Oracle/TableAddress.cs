using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace SilentScan.Verify.Oracle;

[VerifierSupport]
public sealed partial class TableAddress
{
    private TableAddress(TouchedObject touched, string table)
    {
        Touched = touched;
        Table = table;
    }

    public TouchedObject Touched { get; }

    public string Table { get; }

    public static TableAddress? TryParse(string? qualifiedTable)
    {
        if (qualifiedTable is null)
        {
            return null;
        }

        var parts = qualifiedTable.Split('.').Select(p => p.Trim('[', ']')).ToList();
        if (parts.Count is < 1 or > 2 || parts.Any(p => !PlainIdentifier().IsMatch(p)))
        {
            return null;
        }

        return new TableAddress(
            new TouchedObject(parts.Count == 2 ? parts[0] : null, parts[^1]),
            string.Join('.', parts.Select(p => "[" + p + "]")));
    }

    public static async Task<int> ExecuteAsync(SqlConnection connection, SqlTransaction transaction, string sql, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(sql, connection, transaction) { CommandTimeout = 120 };
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex PlainIdentifier();
}
