namespace SilentScan.Tests.Architecture;

public sealed class OraclePairingBaselineTests
{
    private const int MaxUnpairedRules = 159;

    private static string BaselinePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
        {
            dir = dir.Parent;
        }

        return Path.Combine(dir?.FullName ?? throw new InvalidOperationException("Repo root not found."), "tests", "SilentScan.Tests", "oracle-pairing-baseline.txt");
    }

    [Fact]
    public void BaselineOnlyShrinks()
    {
        var entries = File.ReadAllLines(BaselinePath()).Where(l => l.Trim().Length > 0).ToList();

        Assert.True(
            entries.Count <= MaxUnpairedRules,
            $"The baseline lists {entries.Count} unpaired rules, above the ceiling of {MaxUnpairedRules}. A new rule must be paired through RuleOracle, not added here; lower the ceiling when rules are migrated.");
    }

    [Fact]
    public void BaselineIsSortedAndFreeOfDuplicates()
    {
        var entries = File.ReadAllLines(BaselinePath()).Where(l => l.Trim().Length > 0).ToList();

        Assert.Equal(entries.Distinct(StringComparer.Ordinal).OrderBy(e => e, StringComparer.Ordinal), entries);
    }
}
