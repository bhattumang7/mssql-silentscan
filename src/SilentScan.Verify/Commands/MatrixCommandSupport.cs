using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using SilentScan.Verify.Oracle;

namespace SilentScan.Verify.Commands;

internal static class MatrixCommandSupport
{
    public static readonly JsonSerializerOptions DefaultJsonOptions = new() { WriteIndented = true };

    public static Command Create(
        string name,
        string description,
        string defaultOutput,
        Func<string, SqlServerOptions, TextWriter, CancellationToken, Task<int>> run)
    {
        var outputOption = new Option<string>("--output")
        {
            Description = "Path to write the matrix JSON to.",
            DefaultValueFactory = _ => defaultOutput,
        };

        var command = new Command(name, description);
        command.Options.Add(outputOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var output = parseResult.GetValue(outputOption)!;
            return await run(output, SqlServerOptions.LocalDocker, Console.Out, cancellationToken);
        });

        return command;
    }

    public static string ProbedAtUtc() => DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static async Task<int> WriteAsync(
        string outputPath,
        object document,
        int entryCount,
        string serverVersion,
        TextWriter stdout,
        CancellationToken cancellationToken,
        JsonSerializerOptions? jsonOptions = null)
    {
        var json = JsonSerializer.Serialize(document, jsonOptions ?? DefaultJsonOptions);
        await File.WriteAllTextAsync(outputPath, json + Environment.NewLine, cancellationToken);
        await stdout.WriteLineAsync($"Wrote {entryCount} entries (server {serverVersion}) to {outputPath}");
        return 0;
    }
}
