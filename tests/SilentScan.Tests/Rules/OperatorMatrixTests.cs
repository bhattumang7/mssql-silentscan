using SilentScan.Core.Rules;

namespace SilentScan.Tests.Rules;

public sealed class OperatorMatrixTests
{
    [Theory]
    [InlineData("+", "INT", "DECIMAL(5,1)", "decimal(12,1)")]
    [InlineData("/", "INT", "DECIMAL(5,1)", "decimal(17,6)")]
    [InlineData("*", "DECIMAL(38,10)", "DECIMAL(38,10)", "decimal(38,6)")]
    [InlineData("COALESCE", "INT", "DECIMAL(5,1)", "decimal(11,1)")]
    [InlineData("ISNULL", "INT", "DECIMAL(5,1)", "int")]
    [InlineData("ISNULL", "VARCHAR(5)", "VARCHAR(10)", "varchar(5)")]
    [InlineData("COALESCE", "VARCHAR(5)", "VARCHAR(10)", "varchar(10)")]
    [InlineData("+", "VARCHAR(5)", "VARCHAR(10)", "varchar(15)")]
    [InlineData("UNION", "VARCHAR(MAX)", "NVARCHAR(10)", "nvarchar(max)")]
    public void Result_IsTheTypeTheEngineReports(string op, string left, string right, string expected)
    {
        Assert.Equal(expected, OperatorMatrix.Instance.TryGetResult(op, left, right));
    }

    [Fact]
    public void IllegalPair_IsRecordedAsAnErrorNotAType()
    {
        var result = OperatorMatrix.Instance.TryGetResult("*", "VARCHAR(5)", "VARCHAR(10)");

        Assert.NotNull(result);
        Assert.StartsWith(OperatorMatrix.ErrorPrefix, result, StringComparison.Ordinal);
    }

    [Fact]
    public void IsnullKeepsTheFirstOperandType_WhileCoalesceWidens()
    {
        Assert.NotEqual(
            OperatorMatrix.Instance.TryGetResult("ISNULL", "VARCHAR(5)", "VARCHAR(10)"),
            OperatorMatrix.Instance.TryGetResult("COALESCE", "VARCHAR(5)", "VARCHAR(10)"));
    }

    [Fact]
    public void Matrix_CoversEveryPairOnceWithOneResultPerOperator()
    {
        var matrix = OperatorMatrix.Instance;

        Assert.Equal(1089, matrix.Entries.Count);
        Assert.All(matrix.Entries, e => Assert.Equal(matrix.Operators.Count, e.Results.Count));
    }
}
