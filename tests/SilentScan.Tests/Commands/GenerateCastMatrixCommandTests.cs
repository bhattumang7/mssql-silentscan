using SilentScan.Core.Rules;
using SilentScan.Verify.Commands;
using SilentScan.Verify.Oracle;

namespace SilentScan.Tests.Commands;

public sealed partial class GenerateCastMatrixCommandTests
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

        Assert.Equal(2, CastAssignments().Count(cast));
        Assert.DoesNotContain("TRY_CAST", cast, StringComparison.Ordinal);
        Assert.Equal(2, TryCastAssignments().Count(tryCast));
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"SET @t\d = CAST\(")]
    private static partial System.Text.RegularExpressions.Regex CastAssignments();

    [System.Text.RegularExpressions.GeneratedRegex(@"SET @t\d = TRY_CAST\(")]
    private static partial System.Text.RegularExpressions.Regex TryCastAssignments();
}
