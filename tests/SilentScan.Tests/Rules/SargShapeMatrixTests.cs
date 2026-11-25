using SilentScan.Core.Rules;

namespace SilentScan.Tests.Rules;

public sealed class SargShapeMatrixTests
{
    [Theory]
    [InlineData("eq", "INT", true)]
    [InlineData("between", "DATETIME", true)]
    [InlineData("like-prefix", "VARCHAR(10)", true)]
    [InlineData("like-prefix-variable", "NVARCHAR(10)", true)]
    [InlineData("cast-date", "DATETIME", true)]
    [InlineData("half-open-range", "DATETIME2(3)", true)]
    [InlineData("like-suffix", "VARCHAR(10)", false)]
    [InlineData("like-contains", "VARCHAR(10)", false)]
    [InlineData("isnull-wrap", "INT", false)]
    [InlineData("add-constant", "INT", false)]
    [InlineData("year", "DATE", false)]
    [InlineData("upper", "VARCHAR(10)", false)]
    [InlineData("left", "VARCHAR(10)", false)]
    [InlineData("or-other-column", "INT", false)]
    [InlineData("cast-varchar", "INT", false)]
    public void Shape_PreservesSeekOnlyWhenTheEnginePlansOne(string shape, string columnType, bool expected)
    {
        Assert.Equal(expected, SargShapeMatrix.Instance.PreservesSeek(shape, columnType));
    }

    [Fact]
    public void ImplicitConversionShapes_AreFlaggedAsConvert()
    {
        Assert.Equal("Scan+Convert", SargShapeMatrix.Instance.TryGetOutcome("cast-varchar", "INT"));
        Assert.Equal("Scan+Convert", SargShapeMatrix.Instance.TryGetOutcome("collate-column", "VARCHAR(10)"));
    }

    [Fact]
    public void Matrix_HasNoDuplicatesAndOnlyApplicableShapesPerFamily()
    {
        var entries = SargShapeMatrix.Instance.Entries;

        Assert.Equal(entries.Count, entries.Select(e => (e.Shape, e.ColumnType)).Distinct().Count());
        Assert.DoesNotContain(entries, e => e.Shape == "like-prefix" && e.ColumnType == "INT");
        Assert.DoesNotContain(entries, e => e.Shape == "year" && e.ColumnType.StartsWith("TIME", StringComparison.Ordinal));
    }
}
