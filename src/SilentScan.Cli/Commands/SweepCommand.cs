using System.CommandLine;
using SilentScan.Live.Sweep;
using SilentScan.Verify;

namespace SilentScan.Cli.Commands;

public static class SweepCommand
{
    public static Command Create()
    {
        var outOption = new Option<string>("--out")
        {
            Description = "Directory to write the sweep's JSON and readable text report into.",
            Required = true,
        };

        var ruleOption = new Option<string?>("--rule")
        {
            Description = "Restrict the sweep to one or more rule ids (comma-separated).",
        };

        var command = new Command("sweep", "Deploy every published rule doc example (noncompliant and compliant) through the real scan-db path and report per-rule reachability, false positives, and cross-rule fire matrix. Requires a local disposable SQL Server instance.")
        {
            outOption,
            ruleOption,
        };

        command.SetAction(async parseResult =>
        {
            var outDirectory = parseResult.GetValue(outOption)!;
            var ruleId = parseResult.GetValue(ruleOption);

            Directory.CreateDirectory(outDirectory);

            var cases = RuleExampleCorpus.Build();
            if (ruleId is not null)
            {
                var ruleIds = new HashSet<string>(ruleId.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
                cases = [.. cases.Where(c => ruleIds.Contains(c.RuleId))];
            }

            var results = await SweepRunner.RunAsync(cases, SqlServerOptions.LocalDocker);

            var jsonPath = Path.Combine(outDirectory, "sweep-results.json");
            var textPath = Path.Combine(outDirectory, "sweep-results.txt");
            await File.WriteAllTextAsync(jsonPath, SweepReportWriter.WriteJson(results));
            await File.WriteAllTextAsync(textPath, SweepReportWriter.WriteReadable(results));

            Console.WriteLine(SweepReportWriter.WriteReadable(results));
            Console.WriteLine($"wrote {jsonPath} and {textPath}");
        });

        return command;
    }
}
