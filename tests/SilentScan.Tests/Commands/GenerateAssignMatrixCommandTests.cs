using SilentScan.Verify.Commands;
using SilentScan.Verify.Oracle;

namespace SilentScan.Tests.Commands;

public sealed partial class GenerateAssignMatrixCommandTests
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

        Assert.Equal(source.Values.Count, VariableAssignments().Count(batch));
        Assert.DoesNotContain("ANSI_WARNINGS", batch, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildBatch_ColumnAnsiWarningsOff_TurnsTheOptionOffBeforeEachInsertAndBackOnAfter()
    {
        var source = AssignMatrixGenerator.Types.Single(t => t.Syntax == "NVARCHAR(10)");
        var target = AssignMatrixGenerator.Types.Single(t => t.Syntax == "VARCHAR(5)");

        var batch = AssignMatrixGenerator.BuildBatch(source, target, SilentScan.Core.Rules.AssignContext.ColumnAnsiWarningsOff);

        Assert.Equal(source.Values.Count, AnsiWarningsOff().Count(batch));
        Assert.True(AnsiWarningsOn().Count(batch) >= source.Values.Count);
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"SET @t\d+ = @s\d+;")]
    private static partial System.Text.RegularExpressions.Regex VariableAssignments();

    [System.Text.RegularExpressions.GeneratedRegex(@"SET ANSI_WARNINGS OFF;")]
    private static partial System.Text.RegularExpressions.Regex AnsiWarningsOff();

    [System.Text.RegularExpressions.GeneratedRegex(@"SET ANSI_WARNINGS ON;")]
    private static partial System.Text.RegularExpressions.Regex AnsiWarningsOn();
}
