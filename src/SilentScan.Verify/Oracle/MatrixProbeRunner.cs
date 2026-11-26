using System.Globalization;
using Microsoft.Data.SqlClient;

namespace SilentScan.Verify.Oracle;

internal static class MatrixProbeRunner
{
    public static async Task<IReadOnlyList<string>> RunOutcomesAsync(
        SqlServerOptions options, string database, string batch, int valueCount, string errorPrefix, CancellationToken cancellationToken)
    {
        var outcomes = new string[valueCount];
        try
        {
            await using var connection = new SqlConnection(options.BuildConnectionString(database));
            await connection.OpenAsync(cancellationToken);
            await using var command = new SqlCommand(batch, connection) { CommandTimeout = 120 };
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                outcomes[reader.GetInt32(0)] = reader.GetString(1);
            }
        }
        catch (SqlException ex)
        {
            return Enumerable.Repeat(errorPrefix + ex.Number.ToString(CultureInfo.InvariantCulture), valueCount).ToList();
        }

        return outcomes.Select(o => o ?? "Unknown").ToList();
    }
}
