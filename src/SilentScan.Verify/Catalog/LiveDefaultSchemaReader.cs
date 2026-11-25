using Microsoft.Data.SqlClient;

namespace SilentScan.Verify.Catalog;

public sealed class LiveDefaultSchemaReader
{
    private const string Query = """
        SELECT DISTINCT default_schema_name
        FROM sys.database_principals
        WHERE type IN ('S', 'U', 'G', 'E', 'X') AND default_schema_name IS NOT NULL AND is_fixed_role = 0;
        """;

    private readonly string _connectionString;

    public LiveDefaultSchemaReader(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<IReadOnlyList<string>> ReadAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(Query, connection);
        var schemas = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            schemas.Add(reader.GetString(0));
        }

        return schemas;
    }
}
