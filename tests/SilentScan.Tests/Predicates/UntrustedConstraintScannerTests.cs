using SilentScan.Core.Catalog;
using SilentScan.Core.Predicates;

namespace SilentScan.Tests.Predicates;

public sealed class UntrustedConstraintScannerTests
{
    private static CatalogTable Table(string schema, string name) =>
        new(schema, name, CatalogTableKind.Table, [], [], SourcePath: $"{schema}.{name}", SourceLine: 1);

    [Fact]
    public void UntrustedCheckConstraint_Fires()
    {
        var catalog = new DatabaseCatalog();
        catalog.AddOrReplace(Table("dbo", "Orders"));
        catalog.AddCheckConstraint(new CatalogCheckConstraint("CK_Orders_Amount", "dbo.Orders", IsNotTrusted: true, IsDisabled: false));

        var findings = UntrustedConstraintScanner.Scan(catalog);

        var finding = Assert.Single(findings);
        Assert.Equal(UntrustedConstraintFindingKind.CheckConstraint, finding.Kind);
        Assert.Equal("CK_Orders_Amount", finding.ConstraintName);
    }

    [Fact]
    public void TrustedCheckConstraint_NeverFires()
    {
        var catalog = new DatabaseCatalog();
        catalog.AddCheckConstraint(new CatalogCheckConstraint("CK_Orders_Amount", "dbo.Orders", IsNotTrusted: false, IsDisabled: false));

        Assert.Empty(UntrustedConstraintScanner.Scan(catalog));
    }
}
