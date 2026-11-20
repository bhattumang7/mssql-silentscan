using System.Xml.Linq;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", "silentscan/tvf-fence/nested-under-view-or-tvf")]
public sealed class TvfFenceSoleRowSourceOracleTests : OracleTestFixture
{
    private static readonly string[] ConsumingOperators = ["Nested Loops", "Hash Match", "Merge Join", "Sort", "Stream Aggregate"];

    protected override string DatabaseNameSeed => nameof(TvfFenceSoleRowSourceOracleTests);

    protected override string Ddl => """
        ALTER DATABASE CURRENT SET COMPATIBILITY_LEVEL = 130;
        GO
        CREATE TABLE dbo.Src (Id INT PRIMARY KEY, K INT NOT NULL);
        INSERT INTO dbo.Src SELECT TOP (5000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)), 1 FROM sys.all_objects a, sys.all_objects b;
        GO
        CREATE FUNCTION dbo.MsBig() RETURNS @t TABLE (Id INT, V INT) AS BEGIN INSERT @t SELECT TOP (5000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)), 1 FROM sys.all_objects a, sys.all_objects b; RETURN; END;
        GO
        CREATE VIEW dbo.VMsBig AS SELECT Id, V FROM dbo.MsBig();
        GO
        CREATE VIEW dbo.VMsNested AS SELECT Id, V FROM dbo.VMsBig;
        GO
        """;

    private static IEnumerable<XElement> FenceOutput(XDocument plan) =>
        ShowPlan.Ops(plan, "Table-valued function");

    private static IEnumerable<XElement> OutputScans(XDocument plan) =>
        ShowPlan.Ops(plan, "Table Scan");

    private static long ActualRows(XElement relOp) =>
        relOp.Descendants(ShowPlan.Ns + "RunTimeCountersPerThread").Sum(c => long.Parse((string)c.Attribute("ActualRows")!, System.Globalization.CultureInfo.InvariantCulture));

    private async Task<XDocument> ActualPlanAsync(string probe) => XDocument.Parse(await CaptureActualPlanAsync(probe));

    [Fact]
    public async Task FenceAsTheOnlyRowSource_HasNoOperatorThatConsumesTheFixedEstimate_JoinedControlDoes()
    {
        var sole = await ActualPlanAsync("SELECT Id FROM dbo.VMsBig WHERE V = 1;");
        var nested = await ActualPlanAsync("SELECT Id FROM dbo.VMsNested;");
        var joined = await ActualPlanAsync("SELECT s.K FROM dbo.Src s JOIN dbo.VMsBig v ON v.Id = s.Id;");

        foreach (var plan in new[] { sole, nested })
        {
            Assert.Equal(100, ShowPlan.EstimatedRows(FenceOutput(plan).Single()));
            Assert.DoesNotContain(ShowPlan.PhysicalOps(plan), ConsumingOperators.Contains);
            Assert.Equal(5000, ActualRows(OutputScans(plan).Single()));
        }

        Assert.Equal(100, ShowPlan.EstimatedRows(FenceOutput(joined).Single()));
        Assert.Contains("Nested Loops", ShowPlan.PhysicalOps(joined));
        Assert.Equal(5000, ActualRows(OutputScans(joined).Single()));
        Assert.True(ShowPlan.EstimatedRows(OutputScans(joined).Single()) < 5000);
    }
}
