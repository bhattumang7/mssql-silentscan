using System.IO;
using SilentScan.Core.Predicates;
using SilentScan.Core.Reporting;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Reporting;

public sealed class FindingBaselineTests
{
    private static BareTopNoOrderByFinding Top(string path, int line) => new(path, line, 1, FindingConfidence.High);

    private static ScanReport Report(params BareTopNoOrderByFinding[] findings) =>
        TestScanReports.Build(BareTopNoOrderByFindings: findings);

    private static IReadOnlyList<BareTopNoOrderByFinding> Remaining(ScanReport report) =>
        report.Find<BareTopNoOrderByFinding>("BareTopNoOrderByScanner");

    [Fact]
    public void Apply_ReportIdenticalToCapturedOne_SuppressesEveryFinding()
    {
        var report = Report(Top("dbo.A", 3), Top("dbo.B", 9));

        var result = FindingBaseline.Capture(report).Apply(report);

        Assert.Empty(Remaining(result.Report));
        Assert.Equal(2, result.Suppressed);
    }

    [Fact]
    public void Apply_FindingMovedToAnotherLine_StaysSuppressed()
    {
        var baseline = FindingBaseline.Capture(Report(Top("dbo.A", 3)));

        var result = baseline.Apply(Report(Top("dbo.A", 40)));

        Assert.Empty(Remaining(result.Report));
        Assert.Equal(1, result.Suppressed);
    }

    [Fact]
    public void Apply_ExtraFindingOfSameRuleInSameModule_KeepsOnlyTheExtraOne()
    {
        var baseline = FindingBaseline.Capture(Report(Top("dbo.A", 3)));

        var result = baseline.Apply(Report(Top("dbo.A", 3), Top("dbo.A", 12)));

        var remaining = Assert.Single(Remaining(result.Report));
        Assert.Equal(12, remaining.Line);
        Assert.Equal(1, result.Suppressed);
    }

    [Fact]
    public void Apply_FindingInAnotherModule_IsNotSuppressed()
    {
        var baseline = FindingBaseline.Capture(Report(Top("dbo.A", 3)));

        var result = baseline.Apply(Report(Top("dbo.B", 3)));

        var remaining = Assert.Single(Remaining(result.Report));
        Assert.Equal("dbo.B", remaining.SourcePath);
        Assert.Equal(0, result.Suppressed);
    }

    [Fact]
    public void Apply_FindingOfAnotherRuleInSameModule_IsNotSuppressed()
    {
        var baseline = FindingBaseline.Capture(Report(Top("dbo.A", 3)));
        var other = TestScanReports.Build(CatchAllPredicateFindings: [new CatchAllPredicateFinding("dbo.T", "Col", true, "@p", "dbo.A", 3, 1, FindingConfidence.High)]);

        var result = baseline.Apply(other);

        Assert.Single(result.Report.Find<CatchAllPredicateFinding>("CatchAllPredicateScanner"));
        Assert.Equal(0, result.Suppressed);
    }

    [Fact]
    public void Apply_EmptyBaseline_SuppressesNothing()
    {
        var report = Report(Top("dbo.A", 3));

        var result = FindingBaseline.Capture(Report()).Apply(report);

        Assert.Single(Remaining(result.Report));
        Assert.Equal(0, result.Suppressed);
    }

    [Fact]
    public void Json_RoundTrip_PreservesWhatIsSuppressed()
    {
        var report = Report(Top("dbo.A", 3), Top("dbo.A", 7), Top("dbo.B", 1));

        var loaded = FindingBaseline.FromJson(FindingBaseline.Capture(report).ToJson());
        var result = loaded.Apply(Report(Top("dbo.A", 3), Top("dbo.A", 7), Top("dbo.A", 11), Top("dbo.B", 1)));

        var remaining = Assert.Single(Remaining(result.Report));
        Assert.Equal("dbo.A", remaining.SourcePath);
        Assert.Equal(3, result.Suppressed);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"version\":99,\"entries\":[]}")]
    [InlineData("{\"version\":1}")]
    public void FromJson_MalformedOrUnsupportedDocument_Throws(string json)
    {
        Assert.Throws<InvalidDataException>(() => FindingBaseline.FromJson(json));
    }
}
