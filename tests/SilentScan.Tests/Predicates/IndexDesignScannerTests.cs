using SilentScan.Core.Catalog;
using SilentScan.Core.Predicates;
using SilentScan.Core.TypeInference;

namespace SilentScan.Tests.Predicates;

public sealed class IndexDesignScannerTests
{
    private static CatalogTable Table(
        string schema, string name, IReadOnlyList<CatalogColumn> columns, IReadOnlyList<CatalogIndex> indexes,
        bool isMemoryOptimized = false, IReadOnlyList<CatalogStatisticsInfo>? statistics = null) =>
        new(schema, name, CatalogTableKind.Table, columns, indexes,
            SourcePath: $"{schema}.{name}", SourceLine: 1, IsMemoryOptimized: isMemoryOptimized, Statistics: statistics);

    private static CatalogColumn Column(string name, SqlType? type, bool isNullable = false) =>
        new(name, type, isNullable, IsIdentity: false, IsComputed: false, IsPersisted: false);

    private static readonly SqlType IntType = new(SqlTypeCategory.Int);
    private static readonly SqlType DateType = new(SqlTypeCategory.Date);
    private static readonly SqlType GuidType = new(SqlTypeCategory.UniqueIdentifier);

    [Fact]
    public void HeapWithNonclusteredIndex_Fires()
    {
        var catalog = new DatabaseCatalog();
        var index = new CatalogIndex("IX_Orders_CustomerId", CatalogIndexKind.Index, IsUnique: false, ["CustomerId"], [], IsClustered: false);
        catalog.AddOrReplace(Table("dbo", "Orders", [Column("Id", IntType), Column("CustomerId", IntType)], [index]));

        var findings = IndexDesignScanner.Scan(catalog);

        var finding = Assert.Single(findings);
        Assert.Equal(IndexDesignFindingKind.HeapWithNonclusteredIndexes, finding.Kind);
        Assert.Equal("dbo.Orders", finding.TableQualifiedName);
    }

    [Fact]
    public void HeapWithZeroIndexes_NeverFires()
    {

        var catalog = new DatabaseCatalog();
        catalog.AddOrReplace(Table("dbo", "StagingImport", [Column("Id", IntType)], []));

        var findings = IndexDesignScanner.Scan(catalog);

        Assert.Empty(findings);
    }

    [Fact]
    public void TableWithClusteredIndex_NeverFiresHeapFindings()
    {
        var catalog = new DatabaseCatalog();
        var clustered = new CatalogIndex("PK_Orders", CatalogIndexKind.PrimaryKey, IsUnique: true, ["Id"], [], IsClustered: true);
        var nonclustered = new CatalogIndex("IX_Orders_CustomerId", CatalogIndexKind.Index, IsUnique: false, ["CustomerId"], [], IsClustered: false);
        catalog.AddOrReplace(Table("dbo", "Orders", [Column("Id", IntType), Column("CustomerId", IntType)], [clustered, nonclustered]));

        var findings = IndexDesignScanner.Scan(catalog);

        Assert.Empty(findings);
    }

    [Fact]
    public void HeapOnMemoryOptimizedTable_NeverFires()
    {

        var catalog = new DatabaseCatalog();
        var hashIndex = new CatalogIndex("IX_Orders_CustomerId", CatalogIndexKind.Index, IsUnique: false, ["CustomerId"], [], IsClustered: false);
        catalog.AddOrReplace(Table("dbo", "Orders", [Column("Id", IntType), Column("CustomerId", IntType)], [hashIndex], isMemoryOptimized: true));

        var findings = IndexDesignScanner.Scan(catalog);

        Assert.Empty(findings);
    }

    [Fact]
    public void ClusteredColumnstoreTable_NeverReadAsHeap()
    {

        var catalog = new DatabaseCatalog();
        var cci = new CatalogIndex(null, CatalogIndexKind.Index, IsUnique: false, [], [], IsColumnstore: true, IsClustered: true);
        var secondaryIndex = new CatalogIndex("IX_Orders_CustomerId", CatalogIndexKind.Index, IsUnique: false, ["CustomerId"], [], IsClustered: false);
        catalog.AddOrReplace(Table("dbo", "Orders", [Column("Id", IntType), Column("CustomerId", IntType)], [cci, secondaryIndex]));

        var findings = IndexDesignScanner.Scan(catalog);

        Assert.Empty(findings);
    }

    [Fact]
    public void ClusteredColumnstore_NeverFiresNonUniqueOrWideOrGuidKinds()
    {

        var catalog = new DatabaseCatalog();
        var cci = new CatalogIndex(null, CatalogIndexKind.Index, IsUnique: false, [], [], IsColumnstore: true, IsClustered: true);
        catalog.AddOrReplace(Table("dbo", "Orders", [Column("Id", IntType)], [cci]));

        var findings = IndexDesignScanner.Scan(catalog);

        Assert.Empty(findings);
    }

    [Fact]
    public void GuidClusteredKeyWithNewIdDefault_Fires()
    {
        var catalog = new DatabaseCatalog();
        var clustered = new CatalogIndex("PK_Orders", CatalogIndexKind.PrimaryKey, IsUnique: true, ["Id"], [], IsClustered: true);
        catalog.AddOrReplace(Table("dbo", "Orders", [Column("Id", GuidType)], [clustered]));
        catalog.AddSchemaExpression(new SchemaExpressionReference(
            SchemaDependencyKind.DefaultConstraint, "dbo.Orders", "Id", "(newid())", "dbo.Orders", 1));

        var findings = IndexDesignScanner.Scan(catalog);

        var finding = Assert.Single(findings, f => f.Kind == IndexDesignFindingKind.RandomClusteredKeyGuidDefault);
        Assert.Equal(FindingConfidence.High, finding.Confidence);
    }

    [Fact]
    public void GuidClusteredKeyWithNewSequentialIdDefault_NeverFires()
    {

        var catalog = new DatabaseCatalog();
        var clustered = new CatalogIndex("PK_Orders", CatalogIndexKind.PrimaryKey, IsUnique: true, ["Id"], [], IsClustered: true);
        catalog.AddOrReplace(Table("dbo", "Orders", [Column("Id", GuidType)], [clustered]));
        catalog.AddSchemaExpression(new SchemaExpressionReference(
            SchemaDependencyKind.DefaultConstraint, "dbo.Orders", "Id", "(newsequentialid())", "dbo.Orders", 1));

        var findings = IndexDesignScanner.Scan(catalog);

        Assert.DoesNotContain(findings, f => f.Kind == IndexDesignFindingKind.RandomClusteredKeyGuidDefault);
    }

    [Fact]
    public void GuidClusteredKeyWithNoDefault_NeverFires()
    {
        var catalog = new DatabaseCatalog();
        var clustered = new CatalogIndex("PK_Orders", CatalogIndexKind.PrimaryKey, IsUnique: true, ["Id"], [], IsClustered: true);
        catalog.AddOrReplace(Table("dbo", "Orders", [Column("Id", GuidType)], [clustered]));

        var findings = IndexDesignScanner.Scan(catalog);

        Assert.DoesNotContain(findings, f => f.Kind == IndexDesignFindingKind.RandomClusteredKeyGuidDefault);
    }

    [Fact]
    public void NonGuidClusteredKeyWithNewIdDefault_NeverFires()
    {

        var catalog = new DatabaseCatalog();
        var clustered = new CatalogIndex("PK_Orders", CatalogIndexKind.PrimaryKey, IsUnique: true, ["Id"], [], IsClustered: true);
        catalog.AddOrReplace(Table("dbo", "Orders", [Column("Id", IntType)], [clustered]));
        catalog.AddSchemaExpression(new SchemaExpressionReference(
            SchemaDependencyKind.DefaultConstraint, "dbo.Orders", "Id", "(newid())", "dbo.Orders", 1));

        var findings = IndexDesignScanner.Scan(catalog);

        Assert.DoesNotContain(findings, f => f.Kind == IndexDesignFindingKind.RandomClusteredKeyGuidDefault);
    }

    [Theory]
    [InlineData("(newid())")]
    [InlineData("newid()")]
    [InlineData(" ( NEWID ( ) ) ")]
    public void NewIdDefaultTextVariants_AllRecognized(string definitionText)
    {
        var catalog = new DatabaseCatalog();
        var clustered = new CatalogIndex("PK_Orders", CatalogIndexKind.PrimaryKey, IsUnique: true, ["Id"], [], IsClustered: true);
        catalog.AddOrReplace(Table("dbo", "Orders", [Column("Id", GuidType)], [clustered]));
        catalog.AddSchemaExpression(new SchemaExpressionReference(
            SchemaDependencyKind.DefaultConstraint, "dbo.Orders", "Id", definitionText, "dbo.Orders", 1));

        var findings = IndexDesignScanner.Scan(catalog);

        Assert.Contains(findings, f => f.Kind == IndexDesignFindingKind.RandomClusteredKeyGuidDefault);
    }

    [Fact]
    public void ViewsAndTempTables_NeverScanned()
    {

        var catalog = new DatabaseCatalog();
        var nonclustered = new CatalogIndex("IX", CatalogIndexKind.Index, IsUnique: false, ["A"], [], IsClustered: false);
        catalog.AddOrReplace(new CatalogTable("dbo", "SomeView", CatalogTableKind.TemporaryTable,
            [Column("A", IntType)], [nonclustered], SourcePath: "dbo.SomeView", SourceLine: 1));

        var findings = IndexDesignScanner.Scan(catalog);

        Assert.Empty(findings);
    }

    [Fact]
    public void ExactDuplicateIndexes_Fire()
    {
        var catalog = new DatabaseCatalog();
        var a = new CatalogIndex("IX_A", CatalogIndexKind.Index, IsUnique: false, ["CustomerId"], []);
        var b = new CatalogIndex("IX_B", CatalogIndexKind.Index, IsUnique: false, ["CustomerId"], []);
        catalog.AddOrReplace(Table("dbo", "Orders", [Column("Id", IntType), Column("CustomerId", IntType)], [a, b]));

        var findings = IndexDesignScanner.Scan(catalog);

        var finding = Assert.Single(findings, f => f.Kind == IndexDesignFindingKind.DuplicateIndex);
        Assert.Equal(FindingConfidence.High, finding.Confidence);
    }

    [Fact]
    public void SameKeyWithDifferentIncludedColumns_NeverFiresDuplicate()
    {
        var catalog = new DatabaseCatalog();
        var a = new CatalogIndex("IX_A", CatalogIndexKind.Index, IsUnique: false, ["CustomerId"], ["Status"]);
        var b = new CatalogIndex("IX_B", CatalogIndexKind.Index, IsUnique: false, ["CustomerId"], ["OrderDate"]);
        catalog.AddOrReplace(Table("dbo", "Orders", [Column("Id", IntType), Column("CustomerId", IntType), Column("Status", IntType), Column("OrderDate", IntType)], [a, b]));

        var findings = IndexDesignScanner.Scan(catalog);

        Assert.DoesNotContain(findings, f => f.Kind == IndexDesignFindingKind.DuplicateIndex);
    }

    [Fact]
    public void SameKeyAndSameIncludedColumnsInDifferentOrder_StillFiresDuplicate()
    {
        var catalog = new DatabaseCatalog();
        var a = new CatalogIndex("IX_A", CatalogIndexKind.Index, IsUnique: false, ["CustomerId"], ["Status", "OrderDate"]);
        var b = new CatalogIndex("IX_B", CatalogIndexKind.Index, IsUnique: false, ["CustomerId"], ["OrderDate", "Status"]);
        catalog.AddOrReplace(Table("dbo", "Orders", [Column("Id", IntType), Column("CustomerId", IntType), Column("Status", IntType), Column("OrderDate", IntType)], [a, b]));

        var findings = IndexDesignScanner.Scan(catalog);

        Assert.Single(findings, f => f.Kind == IndexDesignFindingKind.DuplicateIndex);
    }

    [Fact]
    public void SameKeyWithOppositeSortDirection_NeverFiresDuplicate()
    {
        var catalog = new DatabaseCatalog();
        var ascending = new CatalogIndex("IX_A", CatalogIndexKind.Index, IsUnique: false, ["CustomerId"], [], KeyColumnIsDescendingRaw: [false]);
        var descending = new CatalogIndex("IX_B", CatalogIndexKind.Index, IsUnique: false, ["CustomerId"], [], KeyColumnIsDescendingRaw: [true]);
        catalog.AddOrReplace(Table("dbo", "Orders", [Column("Id", IntType), Column("CustomerId", IntType)], [ascending, descending]));

        var findings = IndexDesignScanner.Scan(catalog);

        Assert.DoesNotContain(findings, f => f.Kind == IndexDesignFindingKind.DuplicateIndex);
    }

    [Fact]
    public void DifferentUniquenessOrKind_NeverFiresDuplicate()
    {

        var catalog = new DatabaseCatalog();
        var unique = new CatalogIndex("UQ_A", CatalogIndexKind.UniqueConstraint, IsUnique: true, ["CustomerId"], []);
        var nonUnique = new CatalogIndex("IX_B", CatalogIndexKind.Index, IsUnique: false, ["CustomerId"], []);
        catalog.AddOrReplace(Table("dbo", "Orders", [Column("Id", IntType), Column("CustomerId", IntType)], [unique, nonUnique]));

        var findings = IndexDesignScanner.Scan(catalog);

        Assert.DoesNotContain(findings, f => f.Kind is IndexDesignFindingKind.DuplicateIndex or IndexDesignFindingKind.SubsumedIndex);
    }

    [Fact]
    public void FilteredIndexes_NeverComparedForDuplicateOrSubsumed()
    {

        var catalog = new DatabaseCatalog();
        var a = new CatalogIndex("IX_A", CatalogIndexKind.Index, IsUnique: false, ["CustomerId"], [], IsFiltered: true);
        var b = new CatalogIndex("IX_B", CatalogIndexKind.Index, IsUnique: false, ["CustomerId"], [], IsFiltered: true);
        catalog.AddOrReplace(Table("dbo", "Orders", [Column("Id", IntType), Column("CustomerId", IntType)], [a, b]));

        var findings = IndexDesignScanner.Scan(catalog);

        Assert.DoesNotContain(findings, f => f.Kind is IndexDesignFindingKind.DuplicateIndex or IndexDesignFindingKind.SubsumedIndex);
    }

    [Fact]
    public void PrefixSubsumedIndex_Fires()
    {
        var catalog = new DatabaseCatalog();
        var narrow = new CatalogIndex("IX_Narrow", CatalogIndexKind.Index, IsUnique: false, ["CustomerId"], []);
        var wide = new CatalogIndex("IX_Wide", CatalogIndexKind.Index, IsUnique: false, ["CustomerId", "OrderDate"], []);
        catalog.AddOrReplace(Table("dbo", "Orders", [Column("Id", IntType), Column("CustomerId", IntType), Column("OrderDate", IntType)], [narrow, wide]));

        var findings = IndexDesignScanner.Scan(catalog);

        var finding = Assert.Single(findings, f => f.Kind == IndexDesignFindingKind.SubsumedIndex);
        Assert.Equal("IX_Narrow", finding.IndexName);
    }

    private static IReadOnlyList<IndexDesignFinding> ScanPair(CatalogIndex first, CatalogIndex second)
    {
        var catalog = new DatabaseCatalog();
        catalog.AddOrReplace(Table("dbo", "Orders", [Column("Id", IntType), Column("CustomerId", IntType), Column("OrderDate", IntType)], [first, second]));
        return IndexDesignScanner.Scan(catalog);
    }

    [Fact]
    public void ClusteredIndexAndNonclusteredPrefix_NeverFiresSubsumed()
    {
        var clustered = new CatalogIndex("CX", CatalogIndexKind.Index, IsUnique: false, ["CustomerId"], [], IsClustered: true);
        var wide = new CatalogIndex("IX_Wide", CatalogIndexKind.Index, IsUnique: false, ["CustomerId", "OrderDate"], []);

        Assert.DoesNotContain(ScanPair(clustered, wide), f => f.Kind == IndexDesignFindingKind.SubsumedIndex);
    }

    [Fact]
    public void ClusteredAndNonclusteredWithIdenticalKeys_NeverFiresDuplicate()
    {
        var clustered = new CatalogIndex("CX", CatalogIndexKind.Index, IsUnique: false, ["CustomerId"], [], IsClustered: true);
        var other = new CatalogIndex("IX", CatalogIndexKind.Index, IsUnique: false, ["CustomerId"], []);

        Assert.DoesNotContain(ScanPair(clustered, other), f => f.Kind == IndexDesignFindingKind.DuplicateIndex);
    }

    [Fact]
    public void PartitionedAndUnpartitionedWithIdenticalKeys_NeverFiresDuplicate()
    {
        var partitioned = new CatalogIndex("IX_P", CatalogIndexKind.Index, IsUnique: false, ["CustomerId"], [], PartitionSchemeName: "ps");
        var plain = new CatalogIndex("IX", CatalogIndexKind.Index, IsUnique: false, ["CustomerId"], []);

        Assert.DoesNotContain(ScanPair(partitioned, plain), f => f.Kind == IndexDesignFindingKind.DuplicateIndex);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void XmlSpatialOrHypotheticalIndexesWithIdenticalKeys_NeverFireDuplicate(bool xml, bool spatial, bool hypothetical)
    {
        var first = new CatalogIndex("A", CatalogIndexKind.Index, IsUnique: false, ["CustomerId"], [], IsXmlIndex: xml, IsSpatialIndex: spatial, IsHypothetical: hypothetical);
        var second = new CatalogIndex("B", CatalogIndexKind.Index, IsUnique: false, ["CustomerId"], [], IsXmlIndex: xml, IsSpatialIndex: spatial, IsHypothetical: hypothetical);

        Assert.DoesNotContain(ScanPair(first, second), f => f.Kind == IndexDesignFindingKind.DuplicateIndex);
    }

    [Fact]
    public void TwoPlainNonclusteredIndexesWithIdenticalKeys_StillFireDuplicate()
    {
        var first = new CatalogIndex("A", CatalogIndexKind.Index, IsUnique: false, ["CustomerId"], []);
        var second = new CatalogIndex("B", CatalogIndexKind.Index, IsUnique: false, ["CustomerId"], []);

        Assert.Single(ScanPair(first, second), f => f.Kind == IndexDesignFindingKind.DuplicateIndex);
    }

    [Fact]
    public void UniquePrefixOfUniqueIndex_NeverFiresSubsumed()
    {
        var catalog = new DatabaseCatalog();
        var narrow = new CatalogIndex("UX_Narrow", CatalogIndexKind.Index, IsUnique: true, ["CustomerId"], []);
        var wide = new CatalogIndex("UX_Wide", CatalogIndexKind.Index, IsUnique: true, ["CustomerId", "OrderDate"], []);
        catalog.AddOrReplace(Table("dbo", "Orders", [Column("Id", IntType), Column("CustomerId", IntType), Column("OrderDate", IntType)], [narrow, wide]));

        var findings = IndexDesignScanner.Scan(catalog);

        Assert.DoesNotContain(findings, f => f.Kind == IndexDesignFindingKind.SubsumedIndex);
    }

    [Fact]
    public void PrefixWithOppositeSortDirection_NeverFiresSubsumed()
    {
        var catalog = new DatabaseCatalog();
        var narrow = new CatalogIndex("IX_Narrow", CatalogIndexKind.Index, IsUnique: false, ["CustomerId"], [], KeyColumnIsDescendingRaw: [true]);
        var wide = new CatalogIndex("IX_Wide", CatalogIndexKind.Index, IsUnique: false, ["CustomerId", "OrderDate"], [], KeyColumnIsDescendingRaw: [false, false]);
        catalog.AddOrReplace(Table("dbo", "Orders", [Column("Id", IntType), Column("CustomerId", IntType), Column("OrderDate", IntType)], [narrow, wide]));

        var findings = IndexDesignScanner.Scan(catalog);

        Assert.DoesNotContain(findings, f => f.Kind == IndexDesignFindingKind.SubsumedIndex);
    }

    [Fact]
    public void PrefixWithMatchingSortDirection_StillFiresSubsumed()
    {
        var catalog = new DatabaseCatalog();
        var narrow = new CatalogIndex("IX_Narrow", CatalogIndexKind.Index, IsUnique: false, ["CustomerId"], [], KeyColumnIsDescendingRaw: [true]);
        var wide = new CatalogIndex("IX_Wide", CatalogIndexKind.Index, IsUnique: false, ["CustomerId", "OrderDate"], [], KeyColumnIsDescendingRaw: [true, false]);
        catalog.AddOrReplace(Table("dbo", "Orders", [Column("Id", IntType), Column("CustomerId", IntType), Column("OrderDate", IntType)], [narrow, wide]));

        var findings = IndexDesignScanner.Scan(catalog);

        Assert.Single(findings, f => f.Kind == IndexDesignFindingKind.SubsumedIndex);
    }

    [Fact]
    public void SameLengthDifferentColumns_NeverFiresSubsumed()
    {
        var catalog = new DatabaseCatalog();
        var a = new CatalogIndex("IX_A", CatalogIndexKind.Index, IsUnique: false, ["CustomerId"], []);
        var b = new CatalogIndex("IX_B", CatalogIndexKind.Index, IsUnique: false, ["OrderDate"], []);
        catalog.AddOrReplace(Table("dbo", "Orders", [Column("Id", IntType), Column("CustomerId", IntType), Column("OrderDate", IntType)], [a, b]));

        var findings = IndexDesignScanner.Scan(catalog);

        Assert.DoesNotContain(findings, f => f.Kind is IndexDesignFindingKind.DuplicateIndex or IndexDesignFindingKind.SubsumedIndex);
    }

    [Fact]
    public void NonPrefixColumnOrder_NeverFiresSubsumed()
    {

        var catalog = new DatabaseCatalog();
        var a = new CatalogIndex("IX_A", CatalogIndexKind.Index, IsUnique: false, ["OrderDate"], []);
        var b = new CatalogIndex("IX_B", CatalogIndexKind.Index, IsUnique: false, ["CustomerId", "OrderDate"], []);
        catalog.AddOrReplace(Table("dbo", "Orders", [Column("Id", IntType), Column("CustomerId", IntType), Column("OrderDate", IntType)], [a, b]));

        var findings = IndexDesignScanner.Scan(catalog);

        Assert.DoesNotContain(findings, f => f.Kind == IndexDesignFindingKind.SubsumedIndex);
    }

    [Fact]
    public void SubsumedIndexWithUncoveredInclude_NeverFires()
    {

        var catalog = new DatabaseCatalog();
        var narrow = new CatalogIndex("IX_Narrow", CatalogIndexKind.Index, IsUnique: false, ["CustomerId"], ["Note"]);
        var wide = new CatalogIndex("IX_Wide", CatalogIndexKind.Index, IsUnique: false, ["CustomerId", "OrderDate"], []);
        catalog.AddOrReplace(Table("dbo", "Orders",
            [Column("Id", IntType), Column("CustomerId", IntType), Column("OrderDate", IntType), Column("Note", IntType)],
            [narrow, wide]));

        var findings = IndexDesignScanner.Scan(catalog);

        Assert.DoesNotContain(findings, f => f.Kind == IndexDesignFindingKind.SubsumedIndex);
    }

    [Fact]
    public void DisabledIndex_ExcludedFromDuplicateComparison()
    {
        var catalog = new DatabaseCatalog();
        var active = new CatalogIndex("IX_Active", CatalogIndexKind.Index, IsUnique: false, ["CustomerId"], []);
        var disabled = new CatalogIndex("IX_Disabled", CatalogIndexKind.Index, IsUnique: false, ["CustomerId"], [], IsDisabled: true);
        catalog.AddOrReplace(Table("dbo", "Orders", [Column("Id", IntType), Column("CustomerId", IntType)], [active, disabled]));

        var findings = IndexDesignScanner.Scan(catalog);

        Assert.DoesNotContain(findings, f => f.Kind is IndexDesignFindingKind.DuplicateIndex or IndexDesignFindingKind.SubsumedIndex);
    }

    [Fact]
    public void UnindexedForeignKey_Fires()
    {
        var catalog = new DatabaseCatalog();
        catalog.AddOrReplace(Table("dbo", "Orders", [Column("Id", IntType), Column("CustomerId", IntType)], []));
        catalog.AddOrReplace(Table("dbo", "Customers", [Column("Id", IntType)], []));
        catalog.AddForeignKey(new ForeignKeyRelationship(
            "FK_Orders_Customers", "dbo.Orders", "CustomerId", "dbo.Customers", "Id"));

        var findings = IndexDesignScanner.Scan(catalog);

        var finding = Assert.Single(findings, f => f.Kind == IndexDesignFindingKind.UnindexedForeignKey);
        Assert.Equal("dbo.Orders", finding.TableQualifiedName);
        Assert.Equal(FindingConfidence.High, finding.Confidence);
    }

    [Fact]
    public void DisabledForeignKeyWithoutIndex_NeverFires_EnabledSiblingStillFires()
    {
        var catalog = new DatabaseCatalog();
        catalog.AddOrReplace(Table("dbo", "Orders", [Column("Id", IntType), Column("CustomerId", IntType), Column("RegionId", IntType)], []));
        catalog.AddOrReplace(Table("dbo", "Customers", [Column("Id", IntType)], []));
        catalog.AddForeignKey(new ForeignKeyRelationship(
            "FK_Orders_Customers", "dbo.Orders", "CustomerId", "dbo.Customers", "Id", IsDisabled: true));
        catalog.AddForeignKey(new ForeignKeyRelationship(
            "FK_Orders_Regions", "dbo.Orders", "RegionId", "dbo.Customers", "Id"));

        var findings = IndexDesignScanner.Scan(catalog);

        var finding = Assert.Single(findings, f => f.Kind == IndexDesignFindingKind.UnindexedForeignKey);
        Assert.Contains("FK_Orders_Regions", finding.DetailText, StringComparison.Ordinal);
    }

    [Fact]
    public void ForeignKeyWithLeadingIndex_NeverFires()
    {
        var catalog = new DatabaseCatalog();
        var leadingIndex = new CatalogIndex("IX_Orders_CustomerId", CatalogIndexKind.Index, IsUnique: false, ["CustomerId"], []);
        catalog.AddOrReplace(Table("dbo", "Orders", [Column("Id", IntType), Column("CustomerId", IntType)], [leadingIndex]));
        catalog.AddOrReplace(Table("dbo", "Customers", [Column("Id", IntType)], []));
        catalog.AddForeignKey(new ForeignKeyRelationship(
            "FK_Orders_Customers", "dbo.Orders", "CustomerId", "dbo.Customers", "Id"));

        var findings = IndexDesignScanner.Scan(catalog);

        Assert.DoesNotContain(findings, f => f.Kind == IndexDesignFindingKind.UnindexedForeignKey);
    }

    [Fact]
    public void CompositeForeignKey_LeadingCompositeIndex_NeverFires()
    {
        var catalog = new DatabaseCatalog();
        var compositeIndex = new CatalogIndex("IX_Orders_Composite", CatalogIndexKind.Index, IsUnique: false, ["CustomerId", "RegionId"], []);
        catalog.AddOrReplace(Table("dbo", "Orders",
            [Column("Id", IntType), Column("CustomerId", IntType), Column("RegionId", IntType)], [compositeIndex]));
        catalog.AddOrReplace(Table("dbo", "Customers", [Column("Id", IntType), Column("RegionId", IntType)], []));
        catalog.AddForeignKey(new ForeignKeyRelationship(
            "FK_Orders_Customers", "dbo.Orders", "CustomerId", "dbo.Customers", "Id"));
        catalog.AddForeignKey(new ForeignKeyRelationship(
            "FK_Orders_Customers", "dbo.Orders", "RegionId", "dbo.Customers", "RegionId"));

        var findings = IndexDesignScanner.Scan(catalog);

        Assert.DoesNotContain(findings, f => f.Kind == IndexDesignFindingKind.UnindexedForeignKey);
    }

    [Fact]
    public void ForeignKeyCoveredOnlyByFilteredIndex_StillFires()
    {

        var catalog = new DatabaseCatalog();
        var filteredIndex = new CatalogIndex("IX_Filtered", CatalogIndexKind.Index, IsUnique: false, ["CustomerId"], [], IsFiltered: true);
        catalog.AddOrReplace(Table("dbo", "Orders", [Column("Id", IntType), Column("CustomerId", IntType)], [filteredIndex]));
        catalog.AddOrReplace(Table("dbo", "Customers", [Column("Id", IntType)], []));
        catalog.AddForeignKey(new ForeignKeyRelationship(
            "FK_Orders_Customers", "dbo.Orders", "CustomerId", "dbo.Customers", "Id"));

        var findings = IndexDesignScanner.Scan(catalog);

        Assert.Contains(findings, f => f.Kind == IndexDesignFindingKind.UnindexedForeignKey);
    }

    [Fact]
    public void FilteredIndex_FilterColumnMissingFromKeyAndInclude_Fires()
    {
        var catalog = new DatabaseCatalog();

        var filtered = new CatalogIndex(
            "IX_Orders_Active", CatalogIndexKind.Index, IsUnique: false, ["CustomerId"], [],
            IsFiltered: true, FilterDefinition: "([IsActive]=(1))");
        catalog.AddOrReplace(Table(
            "dbo", "Orders",
            [Column("CustomerId", IntType), Column("IsActive", new SqlType(SqlTypeCategory.Bit))],
            [filtered]));

        var findings = IndexDesignScanner.Scan(catalog);

        var finding = Assert.Single(findings, f => f.Kind == IndexDesignFindingKind.FilterColumnNotInIndex);
        Assert.Equal(FindingConfidence.High, finding.Confidence);
        Assert.Contains("IsActive", finding.DetailText);
    }

    [Fact]
    public void FilteredIndex_FilterColumnIsKeyColumn_NeverFires()
    {
        var catalog = new DatabaseCatalog();
        var filtered = new CatalogIndex(
            "IX_Orders_Active", CatalogIndexKind.Index, IsUnique: false, ["IsActive"], [],
            IsFiltered: true, FilterDefinition: "([IsActive]=(1))");
        catalog.AddOrReplace(Table(
            "dbo", "Orders", [Column("IsActive", new SqlType(SqlTypeCategory.Bit))], [filtered]));

        var findings = IndexDesignScanner.Scan(catalog);

        Assert.DoesNotContain(findings, f => f.Kind == IndexDesignFindingKind.FilterColumnNotInIndex);
    }

    [Fact]
    public void FilteredIndex_FilterColumnIsIncludeColumn_NeverFires()
    {
        var catalog = new DatabaseCatalog();
        var filtered = new CatalogIndex(
            "IX_Orders_Active", CatalogIndexKind.Index, IsUnique: false, ["CustomerId"], ["IsActive"],
            IsFiltered: true, FilterDefinition: "([IsActive]=(1))");
        catalog.AddOrReplace(Table(
            "dbo", "Orders",
            [Column("CustomerId", IntType), Column("IsActive", new SqlType(SqlTypeCategory.Bit))],
            [filtered]));

        var findings = IndexDesignScanner.Scan(catalog);

        Assert.DoesNotContain(findings, f => f.Kind == IndexDesignFindingKind.FilterColumnNotInIndex);
    }

    [Fact]
    public void FilteredIndex_UnparseableFilterText_NeverGuesses()
    {
        var catalog = new DatabaseCatalog();
        var filtered = new CatalogIndex(
            "IX_Orders_Weird", CatalogIndexKind.Index, IsUnique: false, ["CustomerId"], [],
            IsFiltered: true, FilterDefinition: "not valid t-sql at all (((");
        catalog.AddOrReplace(Table("dbo", "Orders", [Column("CustomerId", IntType)], [filtered]));

        var findings = IndexDesignScanner.Scan(catalog);

        Assert.DoesNotContain(findings, f => f.Kind == IndexDesignFindingKind.FilterColumnNotInIndex);
    }

    [Fact]
    public void StatisticsMarkedNoRecompute_Fires()
    {
        var catalog = new DatabaseCatalog();
        var stats = new CatalogStatisticsInfo("_WA_Sys_CustomerId", NoRecompute: true, IsAutoCreated: true);
        catalog.AddOrReplace(Table("dbo", "Orders", [Column("CustomerId", IntType)], [], statistics: [stats]));

        var findings = IndexDesignScanner.Scan(catalog);

        var finding = Assert.Single(findings);
        Assert.Equal(IndexDesignFindingKind.NoRecomputeStatistics, finding.Kind);
        Assert.Equal("_WA_Sys_CustomerId", finding.IndexName);
        Assert.Equal(FindingConfidence.Medium, finding.Confidence);
    }

    [Fact]
    public void StatisticsWithAutomaticRecompute_NeverFires()
    {
        var catalog = new DatabaseCatalog();
        var stats = new CatalogStatisticsInfo("_WA_Sys_CustomerId", NoRecompute: false, IsAutoCreated: true);
        catalog.AddOrReplace(Table("dbo", "Orders", [Column("CustomerId", IntType)], [], statistics: [stats]));

        var findings = IndexDesignScanner.Scan(catalog);

        Assert.DoesNotContain(findings, f => f.Kind == IndexDesignFindingKind.NoRecomputeStatistics);
    }

    [Fact]
    public void TableWithNoStatisticsInfo_NeverFiresNoRecompute()
    {
        var catalog = new DatabaseCatalog();
        catalog.AddOrReplace(Table("dbo", "Orders", [Column("CustomerId", IntType)], []));

        var findings = IndexDesignScanner.Scan(catalog);

        Assert.DoesNotContain(findings, f => f.Kind == IndexDesignFindingKind.NoRecomputeStatistics);
    }
}
