using SilentScan.Verify.Commands;
using SilentScan.Verify.Oracle;

namespace SilentScan.Tests.Commands;

public sealed class GenerateGuessMatrixCommandTests
{
    [Fact]
    public void Create_HasNameAndCheckedInDefault()
    {
        var command = GenerateGuessMatrixCommand.Create();

        Assert.Equal("generate-guess-matrix", command.Name);
        var option = Assert.Single(command.Options, o => o.Name == "--output");
        Assert.Equal("src/SilentScan.Core/Rules/GuessMatrix.json", option.GetDefaultValue());
    }

    [Fact]
    public void ReadRatio_DividesTheScanEstimateByTheTableSize()
    {
        var plan = "<RelOp NodeId=\"0\" PhysicalOp=\"Table Scan\" LogicalOp=\"Table Scan\" EstimateRows=\"300000\" />";

        Assert.Equal("0.3", GuessMatrixGenerator.ReadRatio(plan));
        Assert.Null(GuessMatrixGenerator.ReadRatio("<RelOp PhysicalOp=\"Constant Scan\" EstimateRows=\"0\" />"));
    }

    [Fact]
    public void Query_UsesAVariableOnlyForTheVariableOperand()
    {
        var shape = GuessMatrixGenerator.Shapes.Single(s => s.Id == "eq");
        var type = GuessMatrixGenerator.ColumnTypes[0];

        Assert.Contains("DECLARE @p INT;", GuessMatrixGenerator.Query("g0", type, shape, GuessMatrixGenerator.VariableOperand, "H"), StringComparison.Ordinal);
        Assert.DoesNotContain("@p", GuessMatrixGenerator.Query("g0", type, shape, GuessMatrixGenerator.LiteralOperand, "H"), StringComparison.Ordinal);
    }
}
