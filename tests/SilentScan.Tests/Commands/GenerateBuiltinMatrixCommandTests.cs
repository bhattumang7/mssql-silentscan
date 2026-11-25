using SilentScan.Verify.Commands;
using SilentScan.Verify.Oracle;

namespace SilentScan.Tests.Commands;

public sealed class GenerateBuiltinMatrixCommandTests
{
    [Fact]
    public void Create_HasNameAndCheckedInDefault()
    {
        var command = GenerateBuiltinMatrixCommand.Create();

        Assert.Equal("generate-builtin-matrix", command.Name);
        var option = Assert.Single(command.Options, o => o.Name == "--output");
        Assert.Equal("src/SilentScan.Core/Rules/BuiltinMatrix.json", option.GetDefaultValue());
    }

    [Fact]
    public void ZeroArgumentProbe_HasOneEmptyShape_AndMultiPositionProbeCrossesTheLists()
    {
        var pi = BuiltinProbes.All.Single(p => p.Function == "PI");
        var left = BuiltinProbes.All.Single(p => p.Function == "LEFT");

        Assert.Single(BuiltinMatrixGenerator.Shapes(pi));
        Assert.Equal("()", BuiltinMatrixGenerator.ShapeText(BuiltinMatrixGenerator.Shapes(pi)[0].Types));
        Assert.Equal(6, BuiltinMatrixGenerator.Shapes(left).Count);
        Assert.Equal("LEFT(a0, a1)", BuiltinMatrixGenerator.Expression(left, ["VARCHAR(20)", "INT"]));
    }
}
