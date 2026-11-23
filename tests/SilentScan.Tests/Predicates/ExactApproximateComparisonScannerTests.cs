using SilentScan.Core.Catalog;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;

namespace SilentScan.Tests.Predicates;

public sealed class ExactApproximateComparisonScannerTests
{
    private const string Ddl = """
        CREATE TABLE dbo.A (Id INT NOT NULL PRIMARY KEY, Big BIGINT NOT NULL, Small INT NOT NULL, Wide DECIMAL(18,2) NOT NULL, Narrow DECIMAL(10,2) NOT NULL, Cash MONEY NOT NULL, Tiny SMALLINT NOT NULL);
        CREATE TABLE dbo.B (Id INT NOT NULL PRIMARY KEY, F FLOAT NOT NULL, R REAL NOT NULL);
        """;

    private static IReadOnlyList<ExactApproximateComparisonFinding> Scan(string sql)
    {
        var result = SqlScriptParser.ParseText("test.sql", $"{Ddl}\nGO\n{sql}");
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        return ExactApproximateComparisonScanner.Scan(result, CatalogBuilder.Build([result]));
    }

    [Theory]
    [InlineData("a.Big = b.F", true)]
    [InlineData("b.F = a.Big", true)]
    [InlineData("a.Big <> b.F", true)]
    [InlineData("a.Big > b.F", true)]
    [InlineData("a.Wide = b.F", true)]
    [InlineData("a.Cash = b.F", true)]
    [InlineData("a.Small = b.R", true)]
    [InlineData("a.Narrow = b.R", true)]
    [InlineData("a.Small = b.F", false)]
    [InlineData("a.Narrow = b.F", false)]
    [InlineData("a.Tiny = b.R", false)]
    [InlineData("a.Id = b.R", true)]
    [InlineData("a.Big = b.Id", false)]
    [InlineData("a.Big = 5", false)]
    public void ExactColumnAgainstApproximateColumn_FiresOnlyWhenTheExactTypeOutrunsThePrecision(string predicate, bool fires)
    {
        var findings = Scan($"SELECT a.Id FROM dbo.A AS a JOIN dbo.B AS b ON {predicate};");

        Assert.Equal(fires, findings.Count == 1);
    }

    [Fact]
    public void FindingNamesBothColumnsAndTypes()
    {
        var finding = Assert.Single(Scan("SELECT a.Id FROM dbo.A AS a, dbo.B AS b WHERE b.F = a.Big;"));

        Assert.Equal("dbo.A.Big", finding.ExactColumn);
        Assert.Equal("dbo.B.F", finding.ApproximateColumn);
        Assert.Equal("BigInt", finding.ExactTypeDisplay);
    }

    [Fact]
    public void ComparisonInUpdateWhere_Fires()
    {
        Assert.Single(Scan("UPDATE a SET Small = 1 FROM dbo.A AS a JOIN dbo.B AS b ON 1 = 1 WHERE a.Big = b.F;"));
    }
}
