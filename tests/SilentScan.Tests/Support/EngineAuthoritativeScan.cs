using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;
using SilentScan.Core.Reporting;
using SilentScan.Live;
using SilentScan.Verify;
using SilentScan.Verify.Deployment;

namespace SilentScan.Tests.Support;

public static class EngineAuthoritativeScan
{
    private static readonly SqlServerOptions Options = SqlServerOptions.LocalDocker;

    public static async Task<ScanReport> ScanAsync(string sql, string? collation = null, CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(sql, collation, cancellationToken);
        return result.Report;
    }

    public static async Task<ScanReport> ScanOnAsync(string sql, SqlServerOptions options, int? compatibilityLevel, CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(sql, null, options, compatibilityLevel, cancellationToken);
        return result.Report;
    }

    public static Task<LiveScanResult> RunAsync(string sql, string? collation = null, CancellationToken cancellationToken = default) =>
        RunAsync(sql, collation, Options, null, cancellationToken);

    private static async Task<LiveScanResult> RunAsync(
        string sql, string? collation, SqlServerOptions options, int? compatibilityLevel, CancellationToken cancellationToken)
    {
        var databaseName = $"SilentScanTest_{Guid.NewGuid():N}";
        var provisioner = new DatabaseProvisioner(options);
        await provisioner.CreateFreshAsync(databaseName, collationName: collation, cancellationToken: cancellationToken);
        try
        {
            if (compatibilityLevel is { } level)
            {
                await provisioner.SetCompatibilityLevelAsync(databaseName, level, cancellationToken);
            }

            await new ScriptDeployer(options).DeployAsync(WrapBareStatementsInProcedures(sql), databaseName, cancellationToken);
            return await LiveScanRunner.RunAsync(options.BuildConnectionString(databaseName), cancellationToken: cancellationToken);
        }
        finally
        {
            await provisioner.DropIfExistsAsync(databaseName, cancellationToken);
        }
    }

    private static string WrapBareStatementsInProcedures(string sql)
    {
        var parseResult = SqlScriptParser.ParseText("fixture.sql", sql);
        if (parseResult.HasErrors || parseResult.Fragment is not TSqlScript { Batches.Count: > 0 } script)
        {

            return sql;
        }

        var rewritten = new System.Text.StringBuilder();
        var harnessCounter = 0;
        var pendingRun = new List<string>();

        void FlushRun()
        {
            if (pendingRun.Count == 0)
            {
                return;
            }

            harnessCounter++;
            rewritten.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"CREATE PROCEDURE dbo.__SilentScanHarness_{harnessCounter} AS");
            rewritten.AppendLine("BEGIN");
            foreach (var batchText in pendingRun)
            {
                rewritten.AppendLine(batchText);
            }

            rewritten.AppendLine("END");
            rewritten.AppendLine("GO");
            pendingRun.Clear();
        }

        foreach (var batch in script.Batches)
        {
            var batchText = sql.Substring(batch.StartOffset, batch.FragmentLength);
            if (batch.Statements.Count > 0 && batch.Statements.All(IsNonDdlStatement))
            {
                pendingRun.Add(batchText);
                continue;
            }

            FlushRun();
            rewritten.AppendLine(batchText);
            rewritten.AppendLine("GO");
        }

        FlushRun();
        return rewritten.ToString();
    }

    private static bool IsNonDdlStatement(TSqlStatement statement)
    {
        if (statement is PredicateSetStatement { Options: var options }
            && (options & (SetOptions.AnsiNulls | SetOptions.QuotedIdentifier)) != 0)
        {
            return false;
        }

        var name = statement.GetType().Name;
        return !name.StartsWith("Create", StringComparison.Ordinal)
            && !name.StartsWith("Alter", StringComparison.Ordinal)
            && !name.StartsWith("Drop", StringComparison.Ordinal);
    }

    public static async Task<ScanReport> ScanFilesAsync(IReadOnlyList<string> sqlFilePaths, string? collation = null, CancellationToken cancellationToken = default)
    {
        var combined = string.Join("\nGO\n", sqlFilePaths.Select(SqlScriptParser.DecodeFile));
        return await ScanAsync(combined, collation, cancellationToken);
    }
}
