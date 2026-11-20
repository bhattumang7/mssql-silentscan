using System.CommandLine;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using SilentScan.Core.Diagnostics;
using SilentScan.Core.Predicates;
using SilentScan.Core.Reporting;
using SilentScan.Core.Reporting.Readable;
using SilentScan.Core.Reporting.Sarif;
using SilentScan.Live;

namespace SilentScan.Cli.Commands;

public readonly record struct ScanFlags(
    bool IncludePlanCacheEvidence,
    bool FetchSqlFromTables,
    bool Strict,
    bool CheckConnectivity = false,
    string? BaselinePath = null,
    string? WriteBaselinePath = null);

public static class ScanDbCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(), new SilentScan.Core.Reporting.FindingJsonConverter() },
    };

    public static Command Create()
    {
        var connectionStringArgument = new Argument<string>("connection-string")
        {
            Description = "A SQL Server connection string (Microsoft.Data.SqlClient format).",
        };

        var formatOption = new Option<string>("--format")
        {
            Description = ReportOutput.FormatOptionDescription,
            DefaultValueFactory = _ => "text",
        };

        var outputOption = new Option<string?>("--output")
        {
            Description = ReportOutput.OutputOptionDescription,
        };

        var planCacheEvidenceOption = new Option<bool>("--plan-cache-evidence")
        {
            Description = ReportOutput.PlanCacheEvidenceOptionDescription,
            DefaultValueFactory = _ => false,
        };

        var fetchSqlFromTablesOption = new Option<bool>("--fetch-sql-from-tables")
        {
            Description = ReportOutput.FetchSqlFromTablesOptionDescription,
            DefaultValueFactory = _ => false,
        };

        var verbosityOption = new Option<string>("--verbosity")
        {
            Description = ReportOutput.VerbosityOptionDescription,
            DefaultValueFactory = _ => "brief",
        };

        var strictOption = new Option<bool>("--strict")
        {
            Description = ReportOutput.StrictOptionDescription,
            DefaultValueFactory = _ => false,
        };

        var checkConnectivityOption = new Option<bool>("--check-connectivity")
        {
            Description = ReportOutput.CheckConnectivityOptionDescription,
            DefaultValueFactory = _ => false,
        };

        var baselineOption = new Option<string?>("--baseline")
        {
            Description = ReportOutput.BaselineOptionDescription,
        };

        var writeBaselineOption = new Option<string?>("--write-baseline")
        {
            Description = ReportOutput.WriteBaselineOptionDescription,
        };

        var description = "Connect to a live SQL Server database, read its catalog from engine metadata, and scan every readable module for performance problems, silent data loss and silently wrong results.\n\nOptions:\n"
            + $"  --format <format> (default: text) - {ReportOutput.FormatOptionDescription}\n"
            + $"  --plan-cache-evidence (default: off) - {ReportOutput.PlanCacheEvidenceOptionDescription}\n"
            + $"  --fetch-sql-from-tables (default: off) - {ReportOutput.FetchSqlFromTablesOptionDescription}\n"
            + $"  --verbosity <verbosity> (default: brief) - {ReportOutput.VerbosityOptionDescription}\n"
            + $"  --strict (default: off) - {ReportOutput.StrictOptionDescription}\n"
            + $"  --check-connectivity (default: off) - {ReportOutput.CheckConnectivityOptionDescription}\n"
            + $"  --baseline <file> - {ReportOutput.BaselineOptionDescription}\n"
            + $"  --write-baseline <file> - {ReportOutput.WriteBaselineOptionDescription}\n"
            + $"  --output <output> - {ReportOutput.OutputOptionDescription}";

        var command = new Command("scan-db", description)
        {
            connectionStringArgument,
            formatOption,
            planCacheEvidenceOption,
            fetchSqlFromTablesOption,
            verbosityOption,
            strictOption,
            checkConnectivityOption,
            baselineOption,
            writeBaselineOption,
            outputOption,
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var connectionString = parseResult.GetValue(connectionStringArgument)!;
            var planCacheEvidence = parseResult.GetValue(planCacheEvidenceOption);
            var fetchSqlFromTables = parseResult.GetValue(fetchSqlFromTablesOption);
            var strict = parseResult.GetValue(strictOption);
            var checkConnectivity = parseResult.GetValue(checkConnectivityOption);
            var options = new ReportOptions(
                parseResult.GetValue(formatOption)!,
                parseResult.GetValue(outputOption),
                parseResult.GetValue(verbosityOption)!);
            return await RunAsync(connectionString, new ScanFlags(planCacheEvidence, fetchSqlFromTables, strict, checkConnectivity, parseResult.GetValue(baselineOption), parseResult.GetValue(writeBaselineOption)), options, Console.Out, Console.Error, cancellationToken);
        });

        return command;
    }

    internal static async Task<int> RunAsync(
        string connectionString, ScanFlags flags, ReportOptions options, TextWriter stdout, TextWriter stderr, CancellationToken cancellationToken = default)
    {
        if (flags.CheckConnectivity)
        {
            return await RunConnectivityCheckAsync(connectionString, stdout, stderr, cancellationToken);
        }

        if (await TryParseOptionsAsync(options, stderr) is not { } parsed)
        {
            return 1;
        }

        var (reportFormat, verbosity) = parsed;

        FindingBaseline? baseline = null;
        if (flags.BaselinePath is { } baselinePath)
        {
            baseline = await TryLoadBaselineAsync(baselinePath, stderr, cancellationToken);
            if (baseline is null)
            {
                return 1;
            }
        }

        var isInteractive = ReferenceEquals(stderr, Console.Error) && !Console.IsErrorRedirected;
        var progress = new TextWriterScanProgress(stderr, isInteractive);
        var overall = Stopwatch.StartNew();

        LiveScanResult result;
        try
        {
            result = await LiveScanRunner.RunAsync(connectionString, flags.IncludePlanCacheEvidence, progress, flags.FetchSqlFromTables, cancellationToken);
        }
        catch (Exception ex) when (ex is Microsoft.Data.SqlClient.SqlException or InvalidOperationException)
        {
            await stderr.WriteLineAsync($"error: could not scan the live database: {ex.Message}");
            return 1;
        }

        await WarnOnParseHealthAsync(result, stderr);

        if (flags.WriteBaselinePath is { } writeBaselinePath
            && !await TryWriteBaselineAsync(writeBaselinePath, result.Report, stderr, cancellationToken))
        {
            return 1;
        }

        result = await ApplyBaselineAsync(baseline, result, stderr);

        string content;
        using (var renderStage = progress.Begin("rendering report"))
        {
            content = reportFormat switch
            {
                ReportFormat.Sarif => SarifReportWriter.Write(result.Report),
                ReportFormat.Json => JsonSerializer.Serialize(result, JsonOptions),
                _ => ReadableLiveScanWriter.Write(result, ReadableLiveScanWriter.DescribeTarget(connectionString), ReportOutput.ToStyle(reportFormat), verbosity),
            };
            renderStage.Complete($"{options.Format}, {content.Length:N0} chars");
        }

        if (!ReportOutput.Emit(content, options.OutputPath, stdout, stderr))
        {
            return 1;
        }

        progress.Done(overall.Elapsed);

        if (result.LineageParity.Mismatches.Count != 0)
        {
            return 1;
        }

        return flags.Strict && ReportOutput.HasCoverageGaps(result.Report) ? 1 : 0;
    }

    private readonly record struct ParsedOptions(ReportFormat Format, ReadableVerbosity Verbosity);

    private static async Task<ParsedOptions?> TryParseOptionsAsync(ReportOptions options, TextWriter stderr)
    {
        if (!ReportOutput.TryParseFormat(options.Format, out var format))
        {
            await stderr.WriteLineAsync(ReportOutput.UnknownFormatMessage(options.Format));
            return null;
        }

        if (!ReportOutput.TryParseVerbosity(options.Verbosity, out var verbosity))
        {
            await stderr.WriteLineAsync(ReportOutput.UnknownVerbosityMessage(options.Verbosity));
            return null;
        }

        return new ParsedOptions(format, verbosity);
    }

    private static async Task<LiveScanResult> ApplyBaselineAsync(FindingBaseline? baseline, LiveScanResult result, TextWriter stderr)
    {
        if (baseline is null)
        {
            return result;
        }

        var applied = baseline.Apply(result.Report);
        await stderr.WriteLineAsync($"baseline: suppressed {applied.Suppressed} known finding(s).");
        return result with { Report = applied.Report };
    }

    private static async Task<FindingBaseline?> TryLoadBaselineAsync(string path, TextWriter stderr, CancellationToken cancellationToken)
    {
        try
        {
            return FindingBaseline.FromJson(await File.ReadAllTextAsync(path, cancellationToken));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            await stderr.WriteLineAsync($"error: could not read --baseline '{path}': {ex.Message}");
            return null;
        }
    }

    private static async Task<bool> TryWriteBaselineAsync(string path, ScanReport report, TextWriter stderr, CancellationToken cancellationToken)
    {
        try
        {
            await File.WriteAllTextAsync(path, FindingBaseline.Capture(report).ToJson(), cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await stderr.WriteLineAsync($"error: could not write --write-baseline '{path}': {ex.Message}");
            return false;
        }
    }

    private static async Task<int> RunConnectivityCheckAsync(string connectionString, TextWriter stdout, TextWriter stderr, CancellationToken cancellationToken)
    {
        try
        {
            var info = await ConnectivityChecker.CheckAsync(connectionString, cancellationToken);
            await stdout.WriteLineAsync($"ok: connected to {info.ServerName}/{info.DatabaseName} (SQL Server {info.ProductVersion}, {info.Edition})");
            return 0;
        }
        catch (Exception ex) when (ex is Microsoft.Data.SqlClient.SqlException or InvalidOperationException)
        {
            await stderr.WriteLineAsync($"error: could not connect to the database: {ex.Message}");
            return 1;
        }
    }

    private static async Task WarnOnParseHealthAsync(LiveScanResult result, TextWriter stderr)
    {
        foreach (var file in result.Report.ParseHealth.Files)
        {
            foreach (var error in file.Errors)
            {
                await stderr.WriteLineAsync($"warning: '{file.Path}' failed to parse: line {error.Line}: {error.Message}");
            }

            foreach (var unanalyzed in file.UnanalyzedBatches)
            {
                var what = unanalyzed.ObjectName is { } name ? $"{unanalyzed.Kind} '{name}'" : "an unidentified object";
                await stderr.WriteLineAsync(
                    $"warning: '{unanalyzed.SourcePath}':{unanalyzed.StartLine} - a batch failed to parse and was dropped; " +
                    $"{what} received zero analysis.");
            }
        }
    }
}
