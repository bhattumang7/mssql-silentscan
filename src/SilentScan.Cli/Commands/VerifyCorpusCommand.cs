using System.CommandLine;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.SqlClient;
using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;
using SilentScan.Live;
using SilentScan.Verify;
using SilentScan.Verify.Catalog;
using SilentScan.Verify.Oracle;

namespace SilentScan.Cli.Commands;

public static class VerifyCorpusCommand
{
    private const int MissLimit = 25;
    private const int PlanParallelism = 4;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static Command Create()
    {
        var connectionStringArgument = new Argument<string>("connection-string")
        {
            Description = "A SQL Server connection string naming the database to scan and probe. Integrated Security=True uses the current Windows login.",
        };

        var outOption = new Option<string>("--out")
        {
            Description = "File to write every per-finding result to as JSON. It contains object names, so keep it local.",
            Required = true,
        };

        var allowScratchIndexOption = new Option<bool>("--allow-scratch-index")
        {
            Description = "Let the verifier create and drop a scratch index on tables whose column has no leading-key index, to verify plan shape. Off by default: the run is then estimated-plan requests only and writes nothing.",
            DefaultValueFactory = _ => false,
        };

        var command = new Command(
            "verify-corpus",
            "Scan a live database, then ask the engine for the estimated plan of a probe for each scan-forced and range-seek finding and report how many show a conversion on the column.")
        {
            connectionStringArgument,
            outOption,
            allowScratchIndexOption,
        };

        command.SetAction(async (parseResult, cancellationToken) =>
            await RunAsync(
                parseResult.GetValue(connectionStringArgument)!,
                parseResult.GetValue(outOption)!,
                parseResult.GetValue(allowScratchIndexOption),
                Console.Out,
                Console.Error,
                cancellationToken));

        return command;
    }

    internal static bool TryParseTarget(string connectionString, out SqlServerOptions options, out string database)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        database = builder.InitialCatalog;
        var dataSource = builder.DataSource;
        if (dataSource.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase))
        {
            dataSource = dataSource[4..];
        }

        var parts = dataSource.Split(',', 2, StringSplitOptions.TrimEntries);
        var port = 1433;
        if (parts.Length == 2 && !int.TryParse(parts[1], out port))
        {
            options = SqlServerOptions.WindowsLogin(parts[0], 0);
            return false;
        }

        options = builder.IntegratedSecurity
            ? SqlServerOptions.WindowsLogin(parts[0], port)
            : new SqlServerOptions(parts[0], port, builder.UserID, builder.Password);
        return database.Length != 0 && !parts[0].Contains('\\');
    }

    private static async Task<int> RunAsync(
        string connectionString, string outPath, bool allowScratchIndex, TextWriter stdout, TextWriter stderr, CancellationToken cancellationToken)
    {
        if (!TryParseTarget(connectionString, out var options, out var database))
        {
            await stderr.WriteLineAsync("error: the connection string needs Initial Catalog and a Server of the form host or host,port (named instances are not supported).");
            return 1;
        }

        var scan = await LiveScanRunner.RunAsync(connectionString, cancellationToken: cancellationToken);
        var findings = scan.Report.FindingsByRuleId.Values
            .SelectMany(f => f)
            .OfType<TypedPredicateFinding>()
            .Where(f => f.Verdict is Verdict.ScanForced or Verdict.RangeSeek)
            .ToList();
        await stderr.WriteLineAsync($"verifying {findings.Count} findings (scratch index {(allowScratchIndex ? "allowed" : "not allowed")})");

        var modules = (await new LiveModuleReader(connectionString).ReadAsync(cancellationToken: cancellationToken))
            .Modules.ToDictionary(m => m.QualifiedName, StringComparer.OrdinalIgnoreCase);
        var moduleVerifier = new ModuleCorpusVerifier(options);
        var rows = new ConcurrentBag<ModuleComparisonRow>();
        var groups = findings.GroupBy(f => f.SourcePath, StringComparer.OrdinalIgnoreCase).ToList();
        await Parallel.ForEachAsync(
            groups,
            new ParallelOptions { MaxDegreeOfParallelism = PlanParallelism, CancellationToken = cancellationToken },
            async (group, token) =>
            {
                modules.TryGetValue(group.Key, out var module);
                foreach (var row in await moduleVerifier.VerifyAsync(database, module, [.. group], token))
                {
                    rows.Add(row);
                }
            });

        var fallbackVerifier = new CorpusFindingVerifier(options, allowScratchIndex);
        var fallback = new List<CorpusFindingResult>();
        foreach (var finding in rows.Where(r => r.Outcome == ModuleComparisonOutcome.NotDecided && r.Finding is not null).Select(r => r.Finding!))
        {
            fallback.Add(await fallbackVerifier.VerifyAsync(database, finding, cancellationToken));
        }

        var fallbackRows = fallback.Select(r => new
        {
            Outcome = CorpusVerificationSummary.Classify(r),
            r.Finding.Verdict,
            TypePair = CorpusVerificationSummary.DescribeTypePair(r.Finding),
            r.Finding.Operator,
            Table = r.Finding.Column.TableQualifiedName,
            Column = r.Finding.Column.ColumnName,
            r.Finding.SourcePath,
            r.Finding.Line,
            r.Detail,
        });
        var orderedRows = rows.ToList();
        await File.WriteAllTextAsync(
            outPath,
            JsonSerializer.Serialize(new { Module = ModuleComparisonSummary.ToJsonRows(orderedRows), Fallback = fallbackRows }, JsonOptions),
            cancellationToken);

        await stdout.WriteLineAsync("module plan comparison (the engine's plan of each module's own definition):");
        await stdout.WriteLineAsync(ModuleComparisonSummary.Render(orderedRows, MissLimit));
        await stdout.WriteLineAsync("rebuilt-probe fallback for findings not decided above (type pair only, not the module's own code):");
        await stdout.WriteLineAsync(CorpusVerificationSummary.Render(fallback, MissLimit));
        await stdout.WriteLineAsync($"wrote {outPath}");
        return 0;
    }
}
