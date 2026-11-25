using SilentScan.Verify.Commands;
using SilentScan.Verify.Oracle;

namespace SilentScan.Tests.Commands;

public sealed class GenerateSetOptionMatrixCommandTests
{
    [Fact]
    public void Create_HasNameAndCheckedInDefault()
    {
        var command = GenerateSetOptionMatrixCommand.Create();

        Assert.Equal("generate-set-option-matrix", command.Name);
        var option = Assert.Single(command.Options, o => o.Name == "--output");
        Assert.Equal("src/SilentScan.Core/Rules/SetOptionMatrix.json", option.GetDefaultValue());
    }

    [Fact]
    public void Probes_HaveUniqueIdsAndSetTheOptionTheyName()
    {
        var probes = SetOptionMatrixGenerator.Probes;

        Assert.Equal(probes.Count, probes.Select(p => p.Id).Distinct().Count());
        Assert.All(probes, p => Assert.Contains(p.Option, p.OnStatement + p.OffStatement, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildBatch_PrependsTheOptionStatement()
    {
        Assert.Equal("SET NOCOUNT OFF; SET ANSI_NULLS ON; SELECT 1", SetOptionMatrixGenerator.BuildBatch("SET ANSI_NULLS ON", "SELECT 1"));
    }
}
