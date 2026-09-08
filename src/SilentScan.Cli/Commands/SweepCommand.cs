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

        var modeOption = new Option<string>("--mode")
        {
            Description = "corpus (default): deploy every example and check reachability/false-positives. metamorphic: apply semantics-preserving mutations to every noncompliant example and check the fired-rule-id set is unchanged.",
            DefaultValueFactory = _ => "corpus",
        };

        var command = new Command("sweep", "Deploy every published rule doc example (noncompliant and compliant) through the real scan-db path and report per-rule reachability, false positives, and cross-rule fire matrix. Requires a local disposable SQL Server instance.")
        {
            outOption,
            ruleOption,
            modeOption,
        };

        command.SetAction(async parseResult =>
        {
            var outDirectory = parseResult.GetValue(outOption)!;
            var ruleId = parseResult.GetValue(ruleOption);
            var mode = parseResult.GetValue(modeOption)!;

            Directory.CreateDirectory(outDirectory);

            var cases = RuleExampleCorpus.Build();
            if (ruleId is not null)
            {
                var ruleIds = new HashSet<string>(ruleId.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
                cases = [.. cases.Where(c => ruleIds.Contains(c.RuleId))];
            }

            if (mode == "metamorphic")
            {
                var metamorphicResults = await MetamorphicSweepRunner.RunAsync(cases, SqlServerOptions.LocalDocker);

                var metamorphicJsonPath = Path.Combine(outDirectory, "metamorphic-results.json");
                var metamorphicTextPath = Path.Combine(outDirectory, "metamorphic-results.txt");
                await File.WriteAllTextAsync(metamorphicJsonPath, SweepReportWriter.WriteJson(metamorphicResults));
                await File.WriteAllTextAsync(metamorphicTextPath, SweepReportWriter.WriteReadable(metamorphicResults));

                Console.WriteLine(SweepReportWriter.WriteReadable(metamorphicResults));
                Console.WriteLine($"wrote {metamorphicJsonPath} and {metamorphicTextPath}");
                return;
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
