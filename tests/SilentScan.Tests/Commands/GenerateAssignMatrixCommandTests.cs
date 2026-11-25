using SilentScan.Verify.Commands;
using SilentScan.Verify.Oracle;

namespace SilentScan.Tests.Commands;

public sealed class GenerateAssignMatrixCommandTests
{
    [Fact]
    public void Create_HasNameAndOutputOptionWithCheckedInDefault()
    {
        var command = GenerateAssignMatrixCommand.Create();

        Assert.Equal("generate-assign-matrix", command.Name);
        var outputOption = Assert.Single(command.Options, o => o.Name == "--output");
        Assert.Equal("src/SilentScan.Core/Rules/AssignMatrix.json", outputOption.GetDefaultValue());
    }

    [Fact]
    public void ProbeTypes_AreUniqueAndEveryTypeHasAtLeastOneValue()
    {
        var syntaxes = AssignMatrixGenerator.Types.Select(t => t.Syntax).ToList();

        Assert.Equal(syntaxes.Count, syntaxes.Distinct(StringComparer.Ordinal).Count());
        Assert.All(AssignMatrixGenerator.Types, t => Assert.NotEmpty(t.Values));
    }

    [Fact]
    public void BuildBatch_VariableContext_AssignsEveryValueAndNeverTouchesAnsiWarnings()
    {
        var source = AssignMatrixGenerator.Types.Single(t => t.Syntax == "NVARCHAR(10)");
        var target = AssignMatrixGenerator.Types.Single(t => t.Syntax == "VARCHAR(10)");

        var batch = AssignMatrixGenerator.BuildBatch(source, target, SilentScan.Core.Rules.AssignContext.Variable);

        Assert.Equal(source.Values.Count, System.Text.RegularExpressions.Regex.Count(batch, @"SET @t\d+ = @s\d+;"));
        Assert.DoesNotContain("ANSI_WARNINGS", batch, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildBatch_ColumnAnsiWarningsOff_TurnsTheOptionOffBeforeEachInsertAndBackOnAfter()
    {
        var source = AssignMatrixGenerator.Types.Single(t => t.Syntax == "NVARCHAR(10)");
        var target = AssignMatrixGenerator.Types.Single(t => t.Syntax == "VARCHAR(5)");

        var batch = AssignMatrixGenerator.BuildBatch(source, target, SilentScan.Core.Rules.AssignContext.ColumnAnsiWarningsOff);

        Assert.Equal(source.Values.Count, System.Text.RegularExpressions.Regex.Count(batch, "SET ANSI_WARNINGS OFF;"));
        Assert.True(System.Text.RegularExpressions.Regex.Count(batch, "SET ANSI_WARNINGS ON;") >= source.Values.Count);
    }
}
