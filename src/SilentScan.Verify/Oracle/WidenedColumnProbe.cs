using Microsoft.Data.SqlClient;

namespace SilentScan.Verify.Oracle;

[VerifierSupport]
internal static class WidenedColumnProbe
{
    private const string DeclaredType = """
        SELECT CONCAT(
            TYPE_NAME(user_type_id),
            CASE
                WHEN TYPE_NAME(user_type_id) IN (N'char', N'varchar', N'binary', N'varbinary') THEN CONCAT(N'(', max_length, N')')
                WHEN TYPE_NAME(user_type_id) IN (N'nchar', N'nvarchar') THEN CONCAT(N'(', max_length / 2, N')')
            END)
        FROM sys.columns
        WHERE object_id = OBJECT_ID(@table) AND name = @column
            AND EXISTS (SELECT 1 FROM sys.sql_expression_dependencies WHERE referencing_id = OBJECT_ID(@module) AND referenced_id = OBJECT_ID(@table));
        """;

    private const string AlterColumn = """
        DECLARE @id int = OBJECT_ID(@table);
        DECLARE @sql nvarchar(max) = N'ALTER TABLE ' + QUOTENAME(OBJECT_SCHEMA_NAME(@id)) + N'.' + QUOTENAME(OBJECT_NAME(@id))
            + N' ALTER COLUMN ' + QUOTENAME(@column) + N' ' + @type + N' NULL;';
        EXEC sys.sp_executesql @sql;
        """;

    public static Perturbation Describe(string? table, string column, string modulePath, Func<string, string> widen)
    {
        if (table is null)
        {
            return Perturbation.NotApplicable("The finding does not name the table of the column it writes.");
        }

        string? original = null;
        return new Perturbation(
            [],
            Difference.ResultChanged,
            async (connection, transaction, cancellationToken) =>
            {
                await using var read = new SqlCommand(DeclaredType, connection, transaction);
                read.Parameters.AddWithValue("@table", table);
                read.Parameters.AddWithValue("@column", column);
                read.Parameters.AddWithValue("@module", modulePath);
                original = await read.ExecuteScalarAsync(cancellationToken) as string;
                return original is null ? 0 : await AlterAsync(connection, transaction, table, column, widen(original), cancellationToken);
            },
            async (connection, transaction, cancellationToken) =>
                original is null ? 0 : await AlterAsync(connection, transaction, table, column, original, cancellationToken),
            static _ => false);
    }

    private static async Task<int> AlterAsync(
        SqlConnection connection, SqlTransaction transaction, string table, string column, string type, CancellationToken cancellationToken)
    {
        await using var alter = new SqlCommand(AlterColumn, connection, transaction);
        alter.Parameters.AddWithValue("@table", table);
        alter.Parameters.AddWithValue("@column", column);
        alter.Parameters.AddWithValue("@type", type);
        await alter.ExecuteNonQueryAsync(cancellationToken);
        return 1;
    }
}
