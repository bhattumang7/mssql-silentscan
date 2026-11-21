using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace SilentScan.Verify.Oracle;

[VerifierSupport]
public sealed partial class ColumnAddress
{
    private ColumnAddress(TouchedObject touched, string table, string column)
    {
        Touched = touched;
        Table = table;
        Column = column;
    }

    public TouchedObject Touched { get; }

    public string Table { get; }

    public string Column { get; }

    public static ColumnAddress? TryParse(string qualifiedTable, string columnName)
    {
        var parts = qualifiedTable.Split('.').Select(p => p.Trim('[', ']')).ToList();
        var column = columnName.Trim('[', ']');
        if (parts.Count is < 1 or > 2 || parts.Any(p => !PlainIdentifier().IsMatch(p)) || !PlainIdentifier().IsMatch(column))
        {
            return null;
        }

        return new ColumnAddress(
            new TouchedObject(parts.Count == 2 ? parts[0] : null, parts[^1]),
            string.Join('.', parts.Select(p => "[" + p + "]")),
            "[" + column + "]");
    }

    public static async Task<int> ExecuteAsync(SqlConnection connection, SqlTransaction transaction, string sql, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(sql, connection, transaction) { CommandTimeout = 120 };
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public bool NamesColumn(SqlException exception) =>
        exception.Number == NullIntoNotNullColumnError && exception.Message.Contains($"'{Column.Trim('[', ']')}'", StringComparison.OrdinalIgnoreCase);

    public const int NullIntoNotNullColumnError = 515;

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex PlainIdentifier();
}
