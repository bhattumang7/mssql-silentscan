using SilentScan.Core.Rules;

namespace SilentScan.Tests.Rules;

public sealed class BuiltinMatrixTests
{
    [Theory]
    [InlineData("LEN", "VARCHAR(MAX)", "bigint")]
    [InlineData("LEN", "VARCHAR(20)", "int")]
    [InlineData("JSON_VALUE", "VARCHAR(20)", "nvarchar(4000)")]
    [InlineData("REPLICATE", "VARCHAR(20);INT", "varchar(8000)")]
    [InlineData("SUM", "DECIMAL(10,2)", "decimal(38,2)")]
    [InlineData("UPPER", "VARCHAR(20) COLLATE Latin1_General_100_CI_AS_SC_UTF8", "varchar(160)")]
    public void ResultType_IsTheTypeTheEngineReports(string function, string shape, string expected)
    {
        Assert.Equal(expected, BuiltinMatrix.Instance.TryGet(function, shape)?.ResultType);
    }

    [Theory]
    [InlineData("GETDATE", "()", "0")]
    [InlineData("RAND", "()", "0")]
    [InlineData("NEWID", "()", "0")]
    [InlineData("PI", "()", "1")]
    [InlineData("LEN", "VARCHAR(20)", "1")]
    public void Determinism_IsTheEnginesComputedColumnProperty(string function, string shape, string expected)
    {
        Assert.Equal(expected, BuiltinMatrix.Instance.TryGet(function, shape)?.Deterministic);
    }

    [Fact]
    public void LenOfMaxTypeWidensWhereLenOfSizedTypeDoesNot()
    {
        Assert.NotEqual(
            BuiltinMatrix.Instance.TryGet("LEN", "VARCHAR(MAX)")!.ResultType,
            BuiltinMatrix.Instance.TryGet("LEN", "VARCHAR(20)")!.ResultType);
    }

    [Fact]
    public void Matrix_HasNoDuplicateFunctionShapePairs()
    {
        var entries = BuiltinMatrix.Instance.Entries;

        Assert.Equal(entries.Count, entries.Select(e => (e.Function, e.Shape)).Distinct().Count());
        Assert.True(entries.Count >= 700);
    }
}
