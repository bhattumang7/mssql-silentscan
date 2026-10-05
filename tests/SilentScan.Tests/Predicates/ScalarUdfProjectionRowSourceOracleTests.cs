using System.Xml.Linq;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", "silentscan/scalar-udf/in-select-or-expression")]
public sealed class ScalarUdfProjectionRowSourceOracleTests : OracleTestFixture
{
    private const int RowCount = 50;

    protected override string DatabaseNameSeed => nameof(ScalarUdfProjectionRowSourceOracleTests);

    protected override string Ddl => $"""
        CREATE TABLE dbo.Source (Id INT NOT NULL PRIMARY KEY);
        GO
        INSERT INTO dbo.Source (Id) SELECT TOP ({RowCount}) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) FROM sys.all_objects;
        GO
        CREATE FUNCTION dbo.Bump(@x INT) RETURNS INT WITH INLINE = OFF AS BEGIN RETURN @x + 1; END;
        GO
        CREATE FUNCTION dbo.Wrap(@x INT) RETURNS TABLE AS RETURN SELECT @x AS V;
        GO
        """;

    private async Task<long> MaxActualRowsOfOperatorsCallingUdfAsync(string probe)
    {
        var plan = XDocument.Parse(await CaptureActualPlanAsync(probe));

        var counts = plan.Descendants()
            .Where(element => element.Name.LocalName == "RelOp")
            .Where(relOp => relOp.Descendants().Any(child => child.Name.LocalName == "UserDefinedFunction"))
            .SelectMany(relOp => relOp.Elements().Where(child => child.Name.LocalName == "RunTimeInformation"))
            .SelectMany(info => info.Descendants().Where(child => child.Name.LocalName == "RunTimeCountersPerThread"))
            .Select(counter => long.Parse((string)counter.Attribute("ActualRows")!, System.Globalization.CultureInfo.InvariantCulture))
            .ToList();

        Assert.NotEmpty(counts);
        return counts.Max();
    }

    [Theory]
    [InlineData("DECLARE @t TABLE (A INT); INSERT INTO @t (A) SELECT dbo.Bump(Id) FROM dbo.Source;")]
    [InlineData("SELECT dbo.Bump(Id) FROM dbo.Source;")]
    public async Task UdfInStatementWithRowSource_RunsOncePerRow(string statement)
    {
        Assert.Equal(RowCount, await MaxActualRowsOfOperatorsCallingUdfAsync(statement));
    }

    [Fact]
    [Trait("Rule", "silentscan/scalar-udf/in-predicate")]
    public async Task UdfInWhereWithRowSource_RunsOncePerRow()
    {
        Assert.Equal(RowCount, await MaxActualRowsOfOperatorsCallingUdfAsync("SELECT Id FROM dbo.Source WHERE dbo.Bump(Id) > 0;"));
    }

    [Fact]
    [Trait("Rule", "silentscan/scalar-udf/in-predicate")]
    public async Task UdfInWhereWithoutRowSource_RunsOnce()
    {
        Assert.Equal(1, await MaxActualRowsOfOperatorsCallingUdfAsync("SELECT 1 AS One WHERE dbo.Bump(1) = 2;"));
    }

    [Fact]
    public async Task UdfInCrossApplyFunctionArgument_RunsOncePerOuterRow()
    {
        Assert.Equal(RowCount, await MaxActualRowsOfOperatorsCallingUdfAsync("SELECT w.V FROM dbo.Source s CROSS APPLY dbo.Wrap(dbo.Bump(s.Id)) w;"));
    }

    [Fact]
    public async Task UdfInUncorrelatedFunctionArgument_RunsOnce()
    {
        Assert.Equal(1, await MaxActualRowsOfOperatorsCallingUdfAsync("DECLARE @v INT = 1; SELECT w.V FROM dbo.Wrap(dbo.Bump(@v)) w;"));
    }

    [Theory]
    [InlineData("DECLARE @t TABLE (A INT); INSERT INTO @t (A) VALUES (dbo.Bump(1));")]
    [InlineData("DECLARE @t TABLE (A INT); INSERT INTO @t (A) SELECT dbo.Bump(1);")]
    public async Task UdfInStatementWithoutRowSource_RunsOnce(string statement)
    {
        Assert.Equal(1, await MaxActualRowsOfOperatorsCallingUdfAsync(statement));
    }
}
