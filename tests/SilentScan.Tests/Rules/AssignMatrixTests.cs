using SilentScan.Core.Rules;

namespace SilentScan.Tests.Rules;

public sealed class AssignMatrixTests
{
    [Fact]
    public void Matrix_CoversEveryPairOfProbeTypes()
    {
        var matrix = AssignMatrix.Instance;
        var types = matrix.Entries.Select(e => e.Source).Distinct().ToList();

        Assert.True(types.Count >= 25);
        Assert.Equal(types.Count * types.Count, matrix.Entries.Count);
    }

    [Fact]
    public void NvarcharToShorterVarchar_VariableTruncatesSilently_ButSameTypeDoesNot()
    {
        var truncating = AssignMatrix.Instance.TryGet("NVARCHAR(10)", "VARCHAR(5)");
        var control = AssignMatrix.Instance.TryGet("NVARCHAR(10)", "NVARCHAR(10)");

        Assert.NotNull(truncating);
        Assert.NotNull(control);
        Assert.True(truncating.SilentlyLoses(AssignContext.Variable));
        Assert.False(control.SilentlyLoses(AssignContext.Variable));
        Assert.All(control.Variable, o => Assert.Equal(AssignCellOutcome.EqualOutcome, o));
    }

    [Fact]
    public void ColumnTruncation_IsLoudWhenAnsiWarningsOn_AndSilentWhenOff()
    {
        var cell = AssignMatrix.Instance.TryGet("NVARCHAR(10)", "VARCHAR(5)");

        Assert.NotNull(cell);
        Assert.Equal("Error:2628", cell.ColumnAnsiWarningsOn[0]);
        Assert.Equal(AssignCellOutcome.LossOutcome, cell.ColumnAnsiWarningsOff[0]);
        Assert.False(cell.IsLoud(AssignContext.ColumnAnsiWarningsOff));
    }

    [Theory]
    [InlineData("BIGINT", "FLOAT")]
    [InlineData("DECIMAL(18,4)", "DECIMAL(5,1)")]
    [InlineData("DATETIMEOFFSET(7)", "DATETIME2(7)")]
    [InlineData("NVARCHAR(10)", "VARCHAR(10)")]
    public void VariableAssignment_LosesSilently(string source, string target)
    {
        var cell = AssignMatrix.Instance.TryGet(source, target);

        Assert.NotNull(cell);
        Assert.True(cell.SilentlyLoses(AssignContext.Variable), $"{source} -> {target} should lose data in a variable assignment");
    }

    [Theory]
    [InlineData("INT", "BIGINT")]
    [InlineData("VARCHAR(10)", "NVARCHAR(10)")]
    [InlineData("DATETIME2(3)", "DATETIME2(7)")]
    [InlineData("TINYINT", "INT")]
    public void WideningAssignment_NeverLoses(string source, string target)
    {
        var cell = AssignMatrix.Instance.TryGet(source, target);

        Assert.NotNull(cell);
        Assert.False(cell.SilentlyLoses(AssignContext.Variable));
        Assert.False(cell.IsLoud(AssignContext.Variable));
    }
}
