using SilentScan.Verify.Commands;
using SilentScan.Verify.Oracle;

namespace SilentScan.Tests.Commands;

public sealed class GenerateOperatorMatrixCommandTests
{
    [Fact]
    public void Create_HasNameAndCheckedInDefault()
    {
        var command = GenerateOperatorMatrixCommand.Create();

        Assert.Equal("generate-operator-matrix", command.Name);
        var option = Assert.Single(command.Options, o => o.Name == "--output");
        Assert.Equal("src/SilentScan.Core/Rules/OperatorMatrix.json", option.GetDefaultValue());
    }

    [Fact]
    public void BuildBatch_WrapsScalarExpressionsAndKeepsSelectsAsIs()
    {
        var left = new AssignProbeType("INT", []);
        var right = new AssignProbeType("DECIMAL(5,1)", []);

        Assert.Equal("DECLARE @l INT; DECLARE @r DECIMAL(5,1); SELECT @l + @r AS r", OperatorMatrixGenerator.BuildBatch(left, right, "@l + @r"));
        Assert.EndsWith("SELECT @l AS r UNION ALL SELECT @r", OperatorMatrixGenerator.BuildBatch(left, right, "SELECT @l AS r UNION ALL SELECT @r"), StringComparison.Ordinal);
    }
}
