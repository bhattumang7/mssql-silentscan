using SilentScan.Core.Catalog;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;

namespace SilentScan.Tests.Predicates;

public sealed class ExpressionDerivedConfidenceTests
{
    private static IReadOnlyList<ExpressionDerivedFinding> Extract(string predicateColumn)
    {
        var sql = $"""
            CREATE TABLE dbo.Orders (OrderId INT NOT NULL PRIMARY KEY, CustomerId INT NOT NULL, Flag INT NOT NULL);
            GO
            CREATE INDEX IX_Orders_CustomerId ON dbo.Orders(CustomerId);
            GO
            CREATE VIEW dbo.vw_OrdersStr AS
            SELECT OrderId, CAST(CustomerId AS VARCHAR(20)) AS CustomerIdStr, CAST(Flag AS VARCHAR(20)) AS FlagStr FROM dbo.Orders;
            GO
            SELECT OrderId FROM dbo.vw_OrdersStr WHERE {predicateColumn} = '5';
            """;
        var result = SqlScriptParser.ParseText("test.sql", sql);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        var catalog = CatalogBuilder.Build([result]);
        var lineage = LineageResolver.Resolve(catalog, [result]);
        return TypedPredicateExtractor.Extract(result, catalog, lineage).ExpressionDerivedFindings;
    }

    [Fact]
    public void ExpressionDerivedOverLeadingIndexedColumn_IsHighConfidence()
    {
        var finding = Assert.Single(Extract("CustomerIdStr"));

        Assert.Equal(FindingConfidence.High, finding.Confidence);
    }

    [Fact]
    public void ExpressionDerivedOverUnindexedColumn_IsNotHighConfidence()
    {
        var finding = Assert.Single(Extract("FlagStr"));

        Assert.NotEqual(FindingConfidence.High, finding.Confidence);
    }
}
