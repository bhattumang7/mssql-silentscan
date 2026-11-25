using SilentScan.Verify.Commands;
using SilentScan.Verify.Oracle;

namespace SilentScan.Tests.Commands;

public sealed class GenerateRescueMatrixCommandTests
{
    [Fact]
    public void Create_HasNameAndCheckedInDefault()
    {
        var command = GenerateRescueMatrixCommand.Create();

        Assert.Equal("generate-rescue-matrix", command.Name);
        Assert.Equal("src/SilentScan.Core/Rules/RescueMatrix.json", Assert.Single(command.Options, o => o.Name == "--output").GetDefaultValue());
    }

    [Theory]
    [InlineData("<RelOp PhysicalOp=\"Index Seek\"/>", "Seek")]
    [InlineData("<RelOp PhysicalOp=\"Index Scan\"/><RelOp PhysicalOp=\"RID Lookup\"/>", "Scan")]
    [InlineData("<RelOp PhysicalOp=\"Constant Scan\"/>", "NoTableAccess")]
    [InlineData("<RelOp PhysicalOp=\"Clustered Index Scan\"/><Warnings><PlanAffectingConvert/></Warnings>", "Scan+Convert")]
    public void Classify_ReadsTheAccessMethodFromThePlan(string plan, string expected)
    {
        Assert.Equal(expected, RescueMatrixGenerator.Classify(plan));
    }

    [Fact]
    public void Scenarios_HaveUniqueIds()
    {
        var ids = RescueScenarios.All.Select(s => s.Id).ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
    }
}
