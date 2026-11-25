using SilentScan.Verify.Commands;
using SilentScan.Verify.Oracle;

namespace SilentScan.Tests.Commands;

public sealed class GenerateSargShapeMatrixCommandTests
{
    [Fact]
    public void Create_HasNameAndCheckedInDefault()
    {
        var command = GenerateSargShapeMatrixCommand.Create();

        Assert.Equal("generate-sarg-matrix", command.Name);
        var option = Assert.Single(command.Options, o => o.Name == "--output");
        Assert.Equal("src/SilentScan.Core/Rules/SargShapeMatrix.json", option.GetDefaultValue());
    }

    [Theory]
    [InlineData("<RelOp PhysicalOp=\"Index Seek\"><SeekPredicates><SeekPredicateNew/></SeekPredicates></RelOp>", "Seek")]
    [InlineData("<RelOp PhysicalOp=\"Index Scan\"></RelOp>", "Scan")]
    [InlineData("<RelOp PhysicalOp=\"Clustered Index Scan\"><Warnings><PlanAffectingConvert/></Warnings></RelOp>", "Scan+Convert")]
    public void Classify_ReadsSeekScanAndConversionFromThePlan(string plan, string expected)
    {
        Assert.Equal(expected, SargShapeMatrixGenerator.Classify(plan));
    }

    [Fact]
    public void Query_DeclaresTheVariableWithoutCollation_AndSubstitutesTheColumnTypeIntoCasts()
    {
        var shape = SargShapes.All.Single(s => s.Id == "cast-same-type");

        Assert.Equal(
            "DECLARE @p VARCHAR(10); SELECT c FROM sg1 WHERE CAST(c AS VARCHAR(10)) = @p",
            SargShapeMatrixGenerator.Query("sg1", "VARCHAR(10) COLLATE Latin1_General_BIN2", shape));
    }
}
