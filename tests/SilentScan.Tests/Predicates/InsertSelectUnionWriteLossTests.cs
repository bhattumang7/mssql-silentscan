using SilentScan.Core.Catalog;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Tests.Predicates;

public sealed class InsertSelectUnionWriteLossTests
{
    private static PredicateExtractionResult Extract(string insertStatement)
    {
        var sql = $"""
            CREATE TABLE dbo.Dest (Code DECIMAL(10,2) NOT NULL);
            CREATE TABLE dbo.Wide (Code DECIMAL(10,4) NOT NULL);
            CREATE TABLE dbo.Narrow (Code DECIMAL(8,2) NOT NULL);
            GO
            CREATE PROCEDURE dbo.p AS
            {insertStatement}
            """;
        var result = SqlScriptParser.ParseText("union.sql", sql);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        var catalog = CatalogBuilder.Build([result]);
        return TypedPredicateExtractor.Extract(result, catalog, LineageResolver.Resolve(catalog, [result]));
    }

    [Theory]
    [InlineData("INSERT INTO dbo.Dest (Code) SELECT w.Code FROM dbo.Wide AS w UNION ALL SELECT n.Code FROM dbo.Narrow AS n;")]
    [InlineData("INSERT INTO dbo.Dest (Code) SELECT n.Code FROM dbo.Narrow AS n UNION SELECT w.Code FROM dbo.Wide AS w;")]
    [InlineData("INSERT INTO dbo.Dest (Code) (SELECT w.Code FROM dbo.Wide AS w);")]
    public void InsertFromUnionOrParenthesizedSelect_FlagsTheTruncatingBranchOnly(string insert)
    {
        var extraction = Extract(insert);

        var finding = Assert.Single(extraction.WriteLossFindings);
        Assert.Equal(WriteLossKind.NumericScaleNarrowing, finding.Kind);
        Assert.DoesNotContain(extraction.SkippedConstructs, s => s.ConstructKind == "write source");
    }

    [Fact]
    public void InsertFromUnionWhereEveryBranchFits_FlagsNothing()
    {
        var extraction = Extract("INSERT INTO dbo.Dest (Code) SELECT n.Code FROM dbo.Narrow AS n UNION ALL SELECT n2.Code FROM dbo.Narrow AS n2;");

        Assert.Empty(extraction.WriteLossFindings);
        Assert.DoesNotContain(extraction.SkippedConstructs, s => s.ConstructKind == "write source");
    }
}
