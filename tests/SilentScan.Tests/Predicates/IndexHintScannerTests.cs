using SilentScan.Core.Catalog;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;
using SilentScan.Core.TypeInference;

namespace SilentScan.Tests.Predicates;

public sealed class IndexHintScannerTests
{
    private static CatalogTable Table(string schema, string name, IReadOnlyList<CatalogColumn> columns, IReadOnlyList<CatalogIndex> indexes) =>
        new(schema, name, CatalogTableKind.Table, columns, indexes, SourcePath: $"{schema}.{name}", SourceLine: 1);

    private static CatalogColumn Col(string name) => new(name, new SqlType(SqlTypeCategory.Int), IsNullable: false, IsIdentity: false, IsComputed: false, IsPersisted: false);

    private static IReadOnlyList<IndexHintFinding> Scan(string sql, DatabaseCatalog catalog)
    {
        var result = SqlScriptParser.ParseText("test.sql", sql);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        return IndexHintScanner.Scan(result, catalog);
    }

    private static DatabaseCatalog CatalogWithIndex()
    {
        var indexes = new List<CatalogIndex>
        {
            new("PK_Orders", CatalogIndexKind.PrimaryKey, IsUnique: true, KeyColumns: ["OrderId"], IncludedColumns: []),
            new("IX_Orders_Status", CatalogIndexKind.Index, IsUnique: false, KeyColumns: ["Status"], IncludedColumns: []),
        };

        var catalog = new DatabaseCatalog();
        catalog.AddOrReplace(Table("dbo", "Orders", [Col("OrderId"), Col("Status")], indexes));
        return catalog;
    }

    [Theory]
    [InlineData("SELECT COUNT(*) FROM dbo.Orders WITH (INDEX(IX_Orders_Status));")]
    [InlineData("SELECT Status FROM dbo.Orders WITH (INDEX(IX_Orders_Status)) ORDER BY Status;")]
    [InlineData("SELECT Status, COUNT(*) FROM dbo.Orders WITH (INDEX(IX_Orders_Status)) GROUP BY Status;")]
    public void HintedIndexCoversAStatementWithNoPredicate_NeverFires(string statement)
    {
        Assert.Empty(Scan(statement, CatalogWithIndex()));
    }

    [Theory]
    [InlineData("SELECT OrderId FROM dbo.Orders WITH (INDEX(IX_Orders_Status)) WHERE OrderId = 1;")]
    [InlineData("SELECT * FROM dbo.Orders WITH (INDEX(IX_Orders_Status));")]
    [InlineData("SELECT OrderId FROM dbo.Orders WITH (INDEX(IX_Orders_Status));")]
    public void HintedIndexThatDoesNotCoverOrIsPairedWithAnotherAccessPath_StillFires(string statement)
    {
        Assert.Single(Scan(statement, CatalogWithIndex()));
    }

    [Theory]
    [InlineData("SELECT OrderId FROM dbo.Orders WITH (INDEX(IX_Orders_Status)) WHERE Status IN (SELECT Status FROM dbo.Orders o2 WHERE o2.OrderId = 1);", false)]
    [InlineData("SELECT OrderId FROM dbo.Orders WITH (INDEX(IX_Orders_Status)) WHERE Status = 3 OR OrderId = 1;", false)]
    [InlineData("SELECT OrderId FROM dbo.Orders WITH (INDEX(IX_Orders_Status)) WHERE Status IN (1, 2);", false)]
    [InlineData("SELECT OrderId FROM dbo.Orders WITH (INDEX(IX_Orders_Status)) WHERE Status > 3;", false)]
    [InlineData("SELECT OrderId FROM dbo.Orders WITH (INDEX(IX_Orders_Status)) WHERE Status IS NOT NULL;", false)]
    public void LeadingColumnNamedInAnyPredicateShape_IsTreatedAsBoundAndNeverFires(string statement, bool fires)
    {
        var findings = Scan(statement, CatalogWithIndex());

        Assert.Equal(fires, findings.Count == 1);
    }

    [Fact]
    public void HintNamesNonexistentIndex_NeverFires()
    {
        var findings = Scan("SELECT 1 FROM dbo.Orders WITH (INDEX(IX_DoesNotExist)) WHERE OrderId = 1;", CatalogWithIndex());

        Assert.Empty(findings);
    }

    [Fact]
    public void HintNamesRealIndexWithUnboundLeadingColumn_Fires()
    {
        var findings = Scan("SELECT 1 FROM dbo.Orders WITH (INDEX(IX_Orders_Status)) WHERE OrderId = 1;", CatalogWithIndex());

        var finding = Assert.Single(findings);
        Assert.Equal(IndexHintFindingKind.HintedIndexNotSeekable, finding.Kind);
        Assert.Equal("IX_Orders_Status", finding.HintedIndexName);
        Assert.Equal("Status", finding.LeadingColumnName);
    }

    [Fact]
    public void HintedIndexLeadingColumnIsTheLeadingOrderByColumn_NeverFires()
    {
        var findings = Scan("SELECT o.OrderId FROM dbo.Orders o WITH (INDEX(IX_Orders_Status)) WHERE o.OrderId > 1 ORDER BY o.Status, o.OrderId;", CatalogWithIndex());

        Assert.Empty(findings);
    }

    [Fact]
    public void HintedIndexLeadingColumnIsOnlyASecondaryOrderByColumn_StillFires()
    {
        var findings = Scan("SELECT o.OrderId FROM dbo.Orders o WITH (INDEX(IX_Orders_Status)) WHERE o.OrderId > 1 ORDER BY o.OrderId, o.Status;", CatalogWithIndex());

        Assert.Single(findings);
    }

    [Fact]
    public void HintNamesRealIndexWithBoundLeadingColumn_NeverFires()
    {
        var findings = Scan("SELECT 1 FROM dbo.Orders WITH (INDEX(IX_Orders_Status)) WHERE Status = 5;", CatalogWithIndex());

        Assert.Empty(findings);
    }

    [Fact]
    public void HintedTableReferencedWithDifferentCasingThanDdl_BoundLeadingColumnStillSuppresses()
    {

        var findings = Scan("SELECT 1 FROM DBO.ORDERS WITH (INDEX(IX_Orders_Status)) WHERE Status = 5;", CatalogWithIndex());

        Assert.Empty(findings);
    }

    [Fact]
    public void CteSharesNameWithHintedRealTable_NeverFires()
    {

        var findings = Scan(
            "WITH Orders AS (SELECT OrderId FROM dbo.Orders) " +
            "SELECT 1 FROM Orders WITH (INDEX(IX_Orders_Status));",
            CatalogWithIndex());

        Assert.Empty(findings);
    }

    [Fact]
    public void NoHints_NeverFires()
    {
        var findings = Scan("SELECT 1 FROM dbo.Orders WHERE OrderId = 1;", CatalogWithIndex());

        Assert.Empty(findings);
    }

    [Fact]
    public void OrdinalIndexHint_DeclinedAsOutOfScope()
    {

        var findings = Scan("SELECT 1 FROM dbo.Orders WITH (INDEX(0)) WHERE OrderId = 1;", CatalogWithIndex());

        Assert.Empty(findings);
    }

    [Fact]
    public void UpdateStatementTargetHint_Fires()
    {

        var findings = Scan("UPDATE o SET OrderId = OrderId FROM dbo.Orders o WITH (INDEX(IX_Orders_Status)) WHERE OrderId = 1;", CatalogWithIndex());

        var finding = Assert.Single(findings);
        Assert.Equal(IndexHintFindingKind.HintedIndexNotSeekable, finding.Kind);
    }

    [Fact]
    public void HintOnJoinedTable_LeadingColumnBoundByJoinCondition_NeverFires()
    {
        var catalog = CatalogWithIndex();
        catalog.AddOrReplace(Table("dbo", "OrderLines", [Col("OrderId")], []));

        var findings = Scan(
            "SELECT 1 FROM dbo.OrderLines ol JOIN dbo.Orders o WITH (INDEX(IX_Orders_Status)) ON o.Status = ol.OrderId;", catalog);

        Assert.Empty(findings);
    }

    [Fact]
    public void HintOnTableUnderCrossApply_Fires()
    {

        var catalog = CatalogWithIndex();
        catalog.AddOrReplace(Table("dbo", "OrderLines", [Col("OrderId")], []));

        var findings = Scan(
            "SELECT o.OrderId FROM dbo.OrderLines ol CROSS APPLY dbo.Orders o WITH (INDEX(IX_Orders_Status));", catalog);

        var finding = Assert.Single(findings);
        Assert.Equal(IndexHintFindingKind.HintedIndexNotSeekable, finding.Kind);
        Assert.Equal("IX_Orders_Status", finding.HintedIndexName);
    }

    [Fact]
    public void LeadingColumnReferencedFromNestedExistsAgainstOuterHintedAlias_CountsAsSeekable()
    {
        var catalog = CatalogWithIndex();
        catalog.AddOrReplace(Table("dbo", "OrderLines", [Col("OrderId")], []));

        var findings = Scan(
            "SELECT 1 FROM dbo.Orders o WITH (INDEX(IX_Orders_Status)) "
            + "WHERE EXISTS (SELECT 1 FROM dbo.OrderLines ol WHERE ol.OrderId = o.Status);", catalog);

        Assert.Empty(findings);
    }

    [Theory]
    [InlineData("ABS(Status) = 5", true)]
    [InlineData("Status + 1 = 5", true)]
    [InlineData("COALESCE(Status, 0) = 5", true)]
    [InlineData("Status LIKE '%5'", true)]
    [InlineData("Status <> 5", false)]
    [InlineData("Status NOT IN (5, 6)", false)]
    [InlineData("CAST(Status AS BIGINT) = 5", false)]
    [InlineData("ISNULL(Status, 0) = 5", false)]
    [InlineData("ABS(Status) = 5 AND Status > 1", false)]
    public void LeadingColumnReachedOnlyThroughAWrapper_IsNotSeekable(string predicate, bool fires)
    {
        var findings = Scan($"SELECT OrderId FROM dbo.Orders WITH (INDEX(IX_Orders_Status)) WHERE {predicate};", CatalogWithIndex());

        Assert.Equal(fires, findings.Any(f => f.Kind == IndexHintFindingKind.HintedIndexNotSeekable));
    }
}
