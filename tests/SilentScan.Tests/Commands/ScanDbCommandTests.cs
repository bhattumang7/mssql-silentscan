using SilentScan.Cli.Commands;

namespace SilentScan.Tests.Commands;

public sealed class ScanDbCommandTests
{
    [Fact]
    public async Task RunAsync_UnknownFormat_ReturnsOneAndWritesErrorWithoutConnecting()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var options = new ReportOptions("xml", null, "brief");

        var exitCode = await ScanDbCommand.RunAsync(
            "Server=nonexistent-host;Database=db", new ScanFlags(false, false, false), options, stdout, stderr, CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Contains("unknown --format", stderr.ToString());
        Assert.Equal(string.Empty, stdout.ToString());
    }

    [Fact]
    public async Task RunAsync_MissingBaselineFile_ReturnsOneAndWritesErrorWithoutConnecting()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var options = new ReportOptions("text", null, "brief");
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "baseline.json");

        var exitCode = await ScanDbCommand.RunAsync(
            "Server=nonexistent-host;Database=db", new ScanFlags(false, false, false, BaselinePath: missing), options, stdout, stderr, CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Contains("could not read --baseline", stderr.ToString());
        Assert.Equal(string.Empty, stdout.ToString());
    }

    [Fact]
    public async Task RunAsync_MalformedBaselineFile_ReturnsOneAndWritesErrorWithoutConnecting()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var options = new ReportOptions("text", null, "brief");
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(path, "not json");

        try
        {
            var exitCode = await ScanDbCommand.RunAsync(
                "Server=nonexistent-host;Database=db", new ScanFlags(false, false, false, BaselinePath: path), options, stdout, stderr, CancellationToken.None);

            Assert.Equal(1, exitCode);
            Assert.Contains("could not read --baseline", stderr.ToString());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task RunAsync_UnknownVerbosity_ReturnsOneAndWritesErrorWithoutConnecting()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var options = new ReportOptions("text", null, "verbose");

        var exitCode = await ScanDbCommand.RunAsync(
            "Server=nonexistent-host;Database=db", new ScanFlags(false, false, false), options, stdout, stderr, CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Contains("unknown --verbosity", stderr.ToString());
    }

    [Fact]
    public async Task RunAsync_ValidOptionsButUnreachableServer_ReturnsOneAndWritesConnectionError()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var options = new ReportOptions("text", null, "brief");

        var exitCode = await ScanDbCommand.RunAsync(
            "Server=127.0.0.1,1;Database=db;Connect Timeout=1;TrustServerCertificate=true",
            new ScanFlags(false, false, false), options, stdout, stderr, CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Contains("error: could not scan the live database", stderr.ToString());
    }

    [Fact]
    public async Task RunAsync_CheckConnectivityUnreachableServer_ReturnsOneAndWritesConnectionErrorWithoutValidatingOtherOptions()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var options = new ReportOptions("not-a-real-format", null, "not-a-real-verbosity");

        var exitCode = await ScanDbCommand.RunAsync(
            "Server=127.0.0.1,1;Database=db;Connect Timeout=1;TrustServerCertificate=true",
            new ScanFlags(false, false, false, CheckConnectivity: true), options, stdout, stderr, CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Contains("error: could not connect to the database", stderr.ToString());
        Assert.DoesNotContain("unknown --format", stderr.ToString());
        Assert.Equal(string.Empty, stdout.ToString());
    }
}
