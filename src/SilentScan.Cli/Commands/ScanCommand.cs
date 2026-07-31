using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Serialization;
using SilentScan.Core.Parsing;
using SilentScan.Core.Reporting;

namespace SilentScan.Cli.Commands;

/// <summary>
/// `silentscan scan &lt;path&gt;` — parses every .sql file under the given folder (or a single
/// file), reports ScriptDOM parse health (Pass 0 / the corpus dialect-sniffing signal), and
/// for files that parsed cleanly, the Tier-1 syntactic non-sargable predicate findings
/// (CLAUDE.md Phase 1 exit criterion). Type/lineage-aware findings land in later phases.
/// </summary>
public static class ScanCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        // Findings schema is versioned JSON (CLAUDE.md) - enum names, not raw ordinals,
        // so the schema stays stable as new SargabilityFindingKind values are added.
        Converters = { new JsonStringEnumConverter() },
    };

    public static Command Create()
    {
        var pathArgument = new Argument<string>("path")
        {
            Description = "A .sql file or a folder to scan recursively.",
        };

        var command = new Command("scan", "Parse .sql files and report parse health plus Tier-1 sargability findings.")
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
        var report = ScanReportBuilder.Build(files);

        stdout.WriteLine(JsonSerializer.Serialize(report, JsonOptions));

        return report.ParseHealth.FilesWithErrors == 0 ? 0 : 1;
    }
}
