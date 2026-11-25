using SilentScan.Core.Rules;
using SilentScan.Verify.Commands;
using SilentScan.Verify.Oracle;

namespace SilentScan.Tests.Commands;

public sealed class GenerateCastMatrixCommandTests
{
    [Fact]
    public void Create_HasNameAndCheckedInDefault()
    {
        var command = GenerateCastMatrixCommand.Create();

        Assert.Equal("generate-cast-matrix", command.Name);
        var option = Assert.Single(command.Options, o => o.Name == "--output");
        Assert.Equal("src/SilentScan.Core/Rules/CastMatrix.json", option.GetDefaultValue());
    }

    [Fact]
    public void BuildBatch_UsesCastOrTryCastOnly()
    {
        var source = new AssignProbeType("INT", ["1", "2"]);
        var target = new AssignProbeType("TINYINT", []);

        var cast = CastMatrixGenerator.BuildBatch(source, target, CastContext.Cast);
        var tryCast = CastMatrixGenerator.BuildBatch(source, target, CastContext.TryCast);

        Assert.Equal(2, System.Text.RegularExpressions.Regex.Count(cast, @"SET @t\d = CAST\("));
        Assert.DoesNotContain("TRY_CAST", cast, StringComparison.Ordinal);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Count(tryCast, @"SET @t\d = TRY_CAST\("));
    }
}
