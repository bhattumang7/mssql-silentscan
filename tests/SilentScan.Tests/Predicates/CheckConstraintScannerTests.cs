using System.Text.Json;
using SilentScan.Core.Catalog;
using SilentScan.Core.Predicates;
using SilentScan.Core.Reporting.Sarif;
using SilentScan.Tests.Support;
using SilentScan.Core.TypeInference;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
public sealed class CheckConstraintScannerTests
{
    private static CatalogTable Table(string schema, string name, IReadOnlyList<CatalogColumn> columns) =>
        new(schema, name, CatalogTableKind.Table, columns, [], SourcePath: $"{schema}.{name}", SourceLine: 1);

    private static CatalogColumn Column(string name, bool isNullable = false, bool isIdentity = false) =>
        new(name, new SqlType(SqlTypeCategory.Int), isNullable, isIdentity, IsComputed: false, IsPersisted: false);

    [Theory]
    [InlineData("(ISNULL([Price],(0))>(0))")]
    [InlineData("(COALESCE([Price],(0))>(0))")]
    [InlineData("([Price] IS DISTINCT FROM NULL)")]
    [InlineData("([Price] IS NOT DISTINCT FROM (5))")]
    [InlineData("([Price] IS DISTINCT FROM (0))")]
    [InlineData("(CASE WHEN [Price]>(0) THEN (1) ELSE (0) END=(1))")]
    [InlineData("(IIF([Price]>(0),(1),(0))=(1))")]
    [InlineData("(CONCAT([Price],'')<>'')")]
    [InlineData("(LEN(ISNULL([Price]+'',''))>(0))")]
    [InlineData("(ISNULL([Price]*(2),(0))>(0))")]
    public void NullableColumnSubstitutedByAConstantInsideThePredicate_TheCheckDecidesNullItself_NeverFires(string definition)
    {
        var catalog = new DatabaseCatalog();
        catalog.AddOrReplace(Table("dbo", "Orders", [Column("Price", isNullable: true)]));
        catalog.AddCheckConstraint(new CatalogCheckConstraint(
            "CK_Orders_Price", "dbo.Orders", IsNotTrusted: false, IsDisabled: false, DefinitionText: definition));

        Assert.Empty(CheckConstraintScanner.Scan(catalog));
    }

    [Fact]
    public void NullableColumn_PredicateWithNoNullGuard_Fires()
    {
        var catalog = new DatabaseCatalog();
        catalog.AddOrReplace(Table("dbo", "Orders", [Column("Price", isNullable: true)]));
        catalog.AddCheckConstraint(new CatalogCheckConstraint(
            "CK_Orders_Price", "dbo.Orders", IsNotTrusted: false, IsDisabled: false, DefinitionText: "([Price]>(0))"));

        var findings = CheckConstraintScanner.Scan(catalog);

        var finding = Assert.Single(findings);
        Assert.Equal(CheckConstraintFindingKind.NullNotHandled, finding.Kind);
        Assert.Equal("CK_Orders_Price", finding.ConstraintName);
        Assert.Equal("Price", finding.ColumnName);
        Assert.Equal(FindingConfidence.High, finding.Confidence);
    }

    [Fact]
    public void NullableColumn_PredicateWithOrBranchNullGuard_NeverFires()
    {

        var catalog = new DatabaseCatalog();
        catalog.AddOrReplace(Table("dbo", "Orders", [Column("Price", isNullable: true)]));
        catalog.AddCheckConstraint(new CatalogCheckConstraint(
            "CK_Orders_Price", "dbo.Orders", IsNotTrusted: false, IsDisabled: false,
            DefinitionText: "([Price] IS NULL OR [Price]>(0))"));

        var findings = CheckConstraintScanner.Scan(catalog);

        Assert.DoesNotContain(findings, f => f.Kind == CheckConstraintFindingKind.NullNotHandled);
    }

    [Fact]
    public void NullableColumn_PredicateWithIsNotNullGuard_NeverFires()
    {

        var catalog = new DatabaseCatalog();
        catalog.AddOrReplace(Table("dbo", "Orders", [Column("Price", isNullable: true)]));
        catalog.AddCheckConstraint(new CatalogCheckConstraint(
            "CK_Orders_Price", "dbo.Orders", IsNotTrusted: false, IsDisabled: false,
            DefinitionText: "([Price] IS NOT NULL AND [Price]>(0))"));

        var findings = CheckConstraintScanner.Scan(catalog);

        Assert.DoesNotContain(findings, f => f.Kind == CheckConstraintFindingKind.NullNotHandled);
    }

    [Fact]
    public void NotNullColumn_NoNullGuardNeeded_NeverFires()
    {
        var catalog = new DatabaseCatalog();
        catalog.AddOrReplace(Table("dbo", "Orders", [Column("Price", isNullable: false)]));
        catalog.AddCheckConstraint(new CatalogCheckConstraint(
            "CK_Orders_Price", "dbo.Orders", IsNotTrusted: false, IsDisabled: false, DefinitionText: "([Price]>(0))"));

        var findings = CheckConstraintScanner.Scan(catalog);

        Assert.Empty(findings);
    }

    [Fact]
    public void MultiColumnTableLevelConstraint_OnlyUnguardedNullableColumnFires()
    {

        var catalog = new DatabaseCatalog();
        catalog.AddOrReplace(Table(
            "dbo", "Orders",
            [Column("A", isNullable: true), Column("B", isNullable: false)]));
        catalog.AddCheckConstraint(new CatalogCheckConstraint(
            "CK_T3", "dbo.Orders", IsNotTrusted: false, IsDisabled: false, DefinitionText: "([A]>[B])"));

        var findings = CheckConstraintScanner.Scan(catalog);

        var finding = Assert.Single(findings);
        Assert.Equal(CheckConstraintFindingKind.NullNotHandled, finding.Kind);
        Assert.Equal("A", finding.ColumnName);
    }

    [Theory]
    [InlineData("(ISNULL([Qty],(0))>(0))")]
    [InlineData("(COALESCE([Qty],(0))>(0))")]
    [InlineData("(CASE WHEN [Qty]>(0) THEN (1) ELSE (0) END=(1))")]
    [InlineData("([dbo].[IsPositive]([Qty])=(1))")]
    [InlineData("([Id]>(0) AND [dbo].[IsPositive]([Qty])=(1) OR [Id]<=(0))")]
    [InlineData("(COALESCE([Qty],[Id]) IS NOT NULL)")]
    public void NullableColumn_OnlyReachedThroughNullReplacingOrUserCode_NeverFires(string definition)
    {
        var catalog = new DatabaseCatalog();
        catalog.AddOrReplace(Table("dbo", "Orders", [Column("Id"), Column("Qty", isNullable: true)]));
        catalog.AddCheckConstraint(new CatalogCheckConstraint(
            "CK_Orders_Qty", "dbo.Orders", IsNotTrusted: false, IsDisabled: false, DefinitionText: definition));

        var findings = CheckConstraintScanner.Scan(catalog);

        Assert.DoesNotContain(findings, f => f.Kind == CheckConstraintFindingKind.NullNotHandled);
    }

    [Theory]
    [InlineData("(LEN([Qty])>(0))")]
    [InlineData("(ABS([Qty])>(0))")]
    [InlineData("(([Qty]+(1))>(0))")]
    [InlineData("(CAST([Qty] AS BIGINT)>(0))")]
    [InlineData("([Qty] IN ((1),(2)))")]
    [InlineData("([Qty] BETWEEN (1) AND (5))")]
    [InlineData("(ISNULL([Id],(0))>(0) AND [Qty]>(0))")]
    public void NullableColumn_ReachedThroughNullPropagatingExpression_StillFires(string definition)
    {
        var catalog = new DatabaseCatalog();
        catalog.AddOrReplace(Table("dbo", "Orders", [Column("Id"), Column("Qty", isNullable: true)]));
        catalog.AddCheckConstraint(new CatalogCheckConstraint(
            "CK_Orders_Qty", "dbo.Orders", IsNotTrusted: false, IsDisabled: false, DefinitionText: definition));

        var findings = CheckConstraintScanner.Scan(catalog);

        var finding = Assert.Single(findings, f => f.Kind == CheckConstraintFindingKind.NullNotHandled);
        Assert.Equal("Qty", finding.ColumnName);
    }

    [Fact]
    public void DisabledConstraint_NeverFires()
    {
        var catalog = new DatabaseCatalog();
        catalog.AddOrReplace(Table("dbo", "Orders", [Column("Price", isNullable: true)]));
        catalog.AddCheckConstraint(new CatalogCheckConstraint(
            "CK_Orders_Price", "dbo.Orders", IsNotTrusted: false, IsDisabled: true, DefinitionText: "([Price]>(0))"));

        Assert.Empty(CheckConstraintScanner.Scan(catalog));
    }

    [Fact]
    public void UnparseableDefinitionText_NeverGuesses()
    {
        var catalog = new DatabaseCatalog();
        catalog.AddOrReplace(Table("dbo", "Orders", [Column("Price", isNullable: true)]));
        catalog.AddCheckConstraint(new CatalogCheckConstraint(
            "CK_Orders_Price", "dbo.Orders", IsNotTrusted: false, IsDisabled: false, DefinitionText: "not valid t-sql at all ((("));

        Assert.Empty(CheckConstraintScanner.Scan(catalog));
    }

    [Fact]
    public void EmptyDefinitionText_NeverFires()
    {
        var catalog = new DatabaseCatalog();
        catalog.AddOrReplace(Table("dbo", "Orders", [Column("Price", isNullable: true)]));
        catalog.AddCheckConstraint(new CatalogCheckConstraint(
            "CK_Orders_Price", "dbo.Orders", IsNotTrusted: false, IsDisabled: false, DefinitionText: ""));

        Assert.Empty(CheckConstraintScanner.Scan(catalog));
    }

    [Fact]
    public void UnknownColumnReferencedInDefinition_IsSkippedNotGuessed()
    {

        var catalog = new DatabaseCatalog();
        catalog.AddOrReplace(Table("dbo", "Orders", []));
        catalog.AddCheckConstraint(new CatalogCheckConstraint(
            "CK_Orders_Ghost", "dbo.Orders", IsNotTrusted: false, IsDisabled: false, DefinitionText: "([Ghost]>(0))"));

        Assert.Empty(CheckConstraintScanner.Scan(catalog));
    }

    [Fact]
    public async Task LiveDeployment_NullableColumnCheckWithNoGuard_Fires()
    {
        var report = await EngineAuthoritativeScan.ScanAsync(
            "CREATE TABLE dbo.CheckNullTarget (Id INT NOT NULL PRIMARY KEY, Price DECIMAL(10,2) NULL, CONSTRAINT CK_CheckNullTarget_Price CHECK (Price > 0));");

        var finding = Assert.Single(report.Find<CheckConstraintFinding>("CheckConstraintScanner"), f => f.Kind == CheckConstraintFindingKind.NullNotHandled);
        Assert.Equal("CK_CheckNullTarget_Price", finding.ConstraintName);
        Assert.Equal("Price", finding.ColumnName);
    }

    [Fact]
    public async Task LiveDeployment_NullableColumnCheckWithOrGuard_NeverFires()
    {
        var report = await EngineAuthoritativeScan.ScanAsync(
            "CREATE TABLE dbo.CheckNullGuarded (Id INT NOT NULL PRIMARY KEY, Price DECIMAL(10,2) NULL, CONSTRAINT CK_CheckNullGuarded_Price CHECK (Price IS NULL OR Price > 0));");

        Assert.DoesNotContain(report.Find<CheckConstraintFinding>("CheckConstraintScanner"), f => f.Kind == CheckConstraintFindingKind.NullNotHandled);
    }

}
