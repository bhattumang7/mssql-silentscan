using SilentScan.Cli.Commands;

namespace SilentScan.Tests.Commands;

public sealed class ScanCommandTests
{
    private static readonly string FixturePath = Path.Combine(
        AppContext.BaseDirectory, "fixtures", "phase0_spike.sql");

    [Fact]
    public void Run_CleanFixture_ReturnsZeroAndWritesJsonReport()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = ScanCommand.Run(FixturePath, stdout, stderr);

        Assert.Equal(0, exitCode);
        Assert.Contains("\"TotalFiles\": 1", stdout.ToString());
        Assert.Empty(stderr.ToString());
    }

    [Fact]
    public void Run_FixtureWithSargabilityIssue_ReportsFindingAsStringEnum()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "tier1", "FUNCTION_WRAPPED_COLUMN_fires.sql");
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = ScanCommand.Run(path, stdout, stderr);

        Assert.Equal(0, exitCode);
        var output = stdout.ToString();
        Assert.Contains("\"Kind\": \"FunctionWrappedColumn\"", output);
        Assert.Contains("\"ColumnName\": \"SomeDate\"", output);
    }

    [Fact]
    public void Run_MissingPath_ReturnsOneAndWritesError()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = ScanCommand.Run("/no/such/path/at/all.sql", stdout, stderr);

        Assert.Equal(1, exitCode);
        Assert.Contains("not found", stderr.ToString());
    }

    [Fact]
    public void Run_MalformedSql_ReturnsOne()
    {
        var tempDir = Directory.CreateTempSubdirectory("silentscan-scancmd-");
        try
        {
            File.WriteAllText(Path.Combine(tempDir.FullName, "broken.sql"), "SELECT FROM WHERE;;;");
            var stdout = new StringWriter();
            var stderr = new StringWriter();

            var exitCode = ScanCommand.Run(tempDir.FullName, stdout, stderr);

            Assert.Equal(1, exitCode);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }
}
