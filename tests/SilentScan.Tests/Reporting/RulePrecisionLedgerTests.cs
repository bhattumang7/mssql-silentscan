using SilentScan.Core.Predicates;
using SilentScan.Core.Reporting;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Reporting;

public sealed class RulePrecisionLedgerTests
{
    [Theory]
    [InlineData(20, 19, 1, true)]
    [InlineData(20, 18, 2, false)]
    [InlineData(20, 0, 0, false)]
    [InlineData(20, 4, 0, false)]
    public void IsDefaultOn_RequiresPrecisionAndEnoughDecidedSamples(int highFindings, int truePositives, int falsePositives, bool expected)
    {
        var entry = new RulePrecisionEntry("silentscan/x/y", highFindings, truePositives, falsePositives, 0);

        Assert.Equal(expected, entry.IsDefaultOn);
    }

    [Fact]
    public void IsDefaultOn_FullyLabelledSmallPopulation_CountsEvenBelowTheMinimum()
    {
        var entry = new RulePrecisionEntry("silentscan/x/y", 3, 3, 0, 0);

        Assert.True(entry.IsDefaultOn);
    }

    [Fact]
    public void UnmeasuredRule_IsNotDefaultOn()
    {
        Assert.False(RulePrecisionLedger.IsDefaultOn("silentscan/never/measured"));
    }

    [Fact]
    public void ApplyDefaultVisibility_AtHighConfidence_HidesRulesBelowThePrecisionGate()
    {
        var report = ReportWithBothRules();

        var visible = RulePrecisionLedger.ApplyDefaultVisibility(report, FindingConfidence.High);

        Assert.Empty(visible.Find<MaxTypedColumnFinding>("MaxTypedColumnScanner"));
        Assert.Single(visible.Find<UntrustedConstraintFinding>("UntrustedConstraintScanner"));
    }

    [Fact]
    public void ApplyDefaultVisibility_AtMediumConfidence_KeepsEverything()
    {
        var report = ReportWithBothRules();

        var visible = RulePrecisionLedger.ApplyDefaultVisibility(report, FindingConfidence.Medium);

        Assert.Single(visible.Find<MaxTypedColumnFinding>("MaxTypedColumnScanner"));
        Assert.Single(visible.Find<UntrustedConstraintFinding>("UntrustedConstraintScanner"));
    }

    private static ScanReport ReportWithBothRules() =>
        TestScanReports.Build(
            MaxTypedColumnFindings: [new MaxTypedColumnFinding("dbo.T", "C", "nvarchar(max)", "dbo.P", 1)],
            UntrustedConstraintFindings: [new UntrustedConstraintFinding(UntrustedConstraintFindingKind.CheckConstraint, "CK", "dbo.T", "dbo.P", 1)]);
}
