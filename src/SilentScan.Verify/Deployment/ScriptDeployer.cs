using Microsoft.Data.SqlClient;

namespace SilentScan.Verify.Deployment;

/// <summary>
/// Deploys a .sql script to the oracle by executing its GO-separated batches sequentially on
/// one connection, so CREATE DATABASE / USE / DDL land in the same session the way sqlcmd
/// would run them (CLAUDE.md Verify: "deploy its DDL to a fresh database").
/// </summary>
public sealed class ScriptDeployer
{
    private readonly SqlServerOptions _options;

    public ScriptDeployer(SqlServerOptions options)
    {
        _options = options;
    }

    public async Task DeployAsync(string script, string? initialDatabase = null, CancellationToken cancellationToken = default)
    {
        var batches = GoBatchSplitter.Split(script);

        await using var connection = new SqlConnection(_options.BuildConnectionString(initialDatabase));
        await connection.OpenAsync(cancellationToken);

        foreach (var batch in batches)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = batch;
            command.CommandTimeout = 60;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
