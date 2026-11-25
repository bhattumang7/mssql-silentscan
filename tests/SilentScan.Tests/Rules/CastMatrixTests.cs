using SilentScan.Core.Rules;

namespace SilentScan.Tests.Rules;

public sealed class CastMatrixTests
{
    [Fact]
    public void WideningCast_IsEqual_AndNarrowingStringCast_Loses()
    {
        var widen = CastMatrix.Instance.TryGet("VARCHAR(5)", "VARCHAR(10)");
        var narrow = CastMatrix.Instance.TryGet("VARCHAR(10)", "VARCHAR(5)");

        Assert.NotNull(widen);
        Assert.NotNull(narrow);
        Assert.False(widen.SilentlyLoses(CastContext.Cast));
        Assert.True(narrow.SilentlyLoses(CastContext.Cast));
        Assert.False(narrow.IsLoud(CastContext.Cast));
    }

    [Fact]
    public void CastOverflowIsLoud_WhileTryCastReturnsNull()
    {
        var cell = CastMatrix.Instance.TryGet("INT", "TINYINT");

        Assert.NotNull(cell);
        Assert.True(cell.IsLoud(CastContext.Cast));
        Assert.Contains(CastCellOutcome.NullOutcome, cell.For(CastContext.TryCast));
        Assert.False(cell.IsLoud(CastContext.TryCast));
    }

    [Fact]
    public void CastToNarrowerString_DiffersFromAssignmentWhichIsLoudUnderAnsiWarnings()
    {
        var cast = CastMatrix.Instance.TryGet("VARCHAR(10)", "VARCHAR(5)");
        var assign = AssignMatrix.Instance.TryGet("VARCHAR(10)", "VARCHAR(5)");

        Assert.NotNull(cast);
        Assert.NotNull(assign);
        Assert.True(cast.SilentlyLoses(CastContext.Cast));
        Assert.True(assign.IsLoud(AssignContext.ColumnAnsiWarningsOn));
    }

    [Theory]
    [InlineData("VARCHAR", "varchar(30)")]
    [InlineData("DECIMAL", "decimal(18,0)")]
    [InlineData("FLOAT(10)", "real")]
    [InlineData("FLOAT(30)", "float")]
    public void UnsizedTargets_ResolveToTheEngineDefaultType(string target, string expected)
    {
        var result = CastMatrix.Instance.TryGetResultType(target);

        Assert.NotNull(result);
        Assert.Equal(expected, result.OfLiteral);
        Assert.Equal(expected, result.OfVariable);
    }

    [Fact]
    public void Matrix_CoversEveryProbedPairOnce()
    {
        Assert.Equal(1089, CastMatrix.Instance.Entries.Count);
        Assert.Equal(CastMatrix.Instance.Entries.Count, CastMatrix.Instance.Entries.Select(e => (e.Source, e.Target)).Distinct().Count());
    }
}
