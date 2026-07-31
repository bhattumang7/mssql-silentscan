using Microsoft.Data.SqlClient;

namespace SilentScan.Verify.Oracle;

/// <summary>
/// Compiles a SELF-AUTHORED probe statement under SET SHOWPLAN_XML ON and captures the
/// resulting estimated plan. This is compile-only: nothing executes, no rows are read or
/// written. CLAUDE.md's Verify workflow requires this rather than SET STATISTICS XML ON
/// (which executes) specifically so that corpus DML/procs are NEVER executed anywhere -
/// only DDL is deployed, tables stay empty, and the probe is ours, never the repo's own code.
/// CONVERT_IMPLICIT is a compile-time artifact, so it is visible in the estimated plan
/// without running the statement.
/// </summary>
public sealed class PlanXmlCapture
{
    private readonly SqlServerOptions _options;

    public PlanXmlCapture(SqlServerOptions options)
    {
        _options = options;
    }

    /// <summary>
    /// Compiles <paramref name="probeStatement"/> (a statement WE authored, never corpus
    /// code) against <paramref name="database"/> and returns its estimated plan XML.
    /// </summary>
    public async Task<string> CaptureAsync(
        string database, string probeStatement, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(_options.BuildConnectionString(database));
        await connection.OpenAsync(cancellationToken);

        // SET SHOWPLAN_XML ON/OFF must each be the only statement in their batch, so they
        // are sent as separate commands from the probe itself.
        await using (var onCommand = connection.CreateCommand())
        {
            onCommand.CommandText = "SET SHOWPLAN_XML ON;";
            await onCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        string planXml;
        try
        {
            await using var probeCommand = connection.CreateCommand();
            probeCommand.CommandText = probeStatement;
            probeCommand.CommandTimeout = 60;

            await using var reader = await probeCommand.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException("SHOWPLAN_XML produced no plan row for the probe statement.");
            }

            planXml = reader.GetString(0);
        }
        finally
        {
            await using var offCommand = connection.CreateCommand();
            offCommand.CommandText = "SET SHOWPLAN_XML OFF;";
            await offCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        return planXml;
    }
}
