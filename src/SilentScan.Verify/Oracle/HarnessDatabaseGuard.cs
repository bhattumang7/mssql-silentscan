using Microsoft.Data.SqlClient;

namespace SilentScan.Verify.Oracle;

[VerifierSupport]
public static class HarnessDatabaseGuard
{
    public const string MarkerName = "SilentScan.HarnessDatabase";

    public static async Task RequireAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            "SELECT DB_NAME(), (SELECT COUNT(*) FROM sys.extended_properties WHERE class = 0 AND name = @marker);",
            connection);
        command.Parameters.AddWithValue("@marker", MarkerName);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        if (reader.GetInt32(1) == 0)
        {
            throw new InvalidOperationException(
                $"Refusing to write: database '{reader.GetString(0)}' was not created by the verification harness.");
        }
    }
}
