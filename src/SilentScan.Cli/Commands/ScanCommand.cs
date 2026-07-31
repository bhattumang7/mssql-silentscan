using System.CommandLine;
using System.Text.Json;
using SilentScan.Core.Parsing;
using SilentScan.Core.Reporting;

namespace SilentScan.Cli.Commands;

/// <summary>
/// `silentscan scan &lt;path&gt;` — Pass 0 of the pipeline: parse every .sql file under the
/// given folder (or a single file) and report ScriptDOM parse health as JSON. This is
/// the dialect-sniffing signal CLAUDE.md's corpus rules key off of; the finding-emitting
/// passes (catalog/lineage/predicates/verdicts) land in Phase 1+ on top of this.
/// </summary>
public static class ScanCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static Command Create()
    {
        var pathArgument = new Argument<string>("path")
        {
            Description = "A .sql file or a folder to scan recursively.",
        };

        var command = new Command("scan", "Parse .sql files and report ScriptDOM parse health.")
        {
            pathArgument,
        };

        command.SetAction(parseResult =>
        {
            var path = parseResult.GetValue(pathArgument)!;
            return Run(path, Console.Out, Console.Error);
        });

        return command;
    }

    internal static int Run(string path, TextWriter stdout, TextWriter stderr)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            stderr.WriteLine($"error: path not found: {path}");
            return 1;
        }

        var files = SqlFileDiscovery.EnumerateSqlFiles(path);
        var report = ParseHealthReportBuilder.Build(files);

        stdout.WriteLine(JsonSerializer.Serialize(report, JsonOptions));

        return report.FilesWithErrors == 0 ? 0 : 1;
    }
}
