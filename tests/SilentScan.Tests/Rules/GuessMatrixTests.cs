using SilentScan.Core.Rules;

namespace SilentScan.Tests.Rules;

public sealed class GuessMatrixTests
{
    [Theory]
    [InlineData("eq", "INT", "Literal", "Default", 0.001)]
    [InlineData("eq", "INT", "Variable", "Legacy", 0.0316228)]
    [InlineData("lt", "VARCHAR(20)", "Literal", "Default", 0.3)]
    [InlineData("ge", "DATE", "Variable", "Default", 0.3)]
    [InlineData("is-not-null", "INT", "Literal", "Default", 0.9)]
    [InlineData("and-2", "INT", "Literal", "Default", 3.16228E-05)]
    public void GuessedFraction_IsWhatTheOptimizerEstimatesWithoutStatistics(string shape, string type, string operand, string estimator, double expected)
    {
        var ratio = GuessMatrix.Instance.TryGetRatio(shape, type, operand, estimator);

        Assert.NotNull(ratio);
        Assert.Equal(expected, ratio.Value, 7);
    }

    [Fact]
    public void ExtraGuessedPredicatesAreBackedOff_SoAndShrinksSlowerThanTheProduct()
    {
        var one = GuessMatrix.Instance.TryGetRatio("eq", "INT", "Literal", "Default")!.Value;
        var two = GuessMatrix.Instance.TryGetRatio("and-2", "INT", "Literal", "Default")!.Value;

        Assert.True(two > one * one);
    }

    [Fact]
    public void Matrix_HasNoDuplicatesAndStatesItsTableSize()
    {
        var entries = GuessMatrix.Instance.Entries;

        Assert.Equal(1_000_000, GuessMatrix.Instance.TableRows);
        Assert.Equal(entries.Count, entries.Select(e => (e.Shape, e.ColumnType, e.Operand, e.Estimator)).Distinct().Count());
    }
}
