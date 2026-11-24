using SilentScan.Core.TypeInference;
using SilentScan.Tests.Support;
using SilentScan.Verify.Catalog;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class LiveCatalogReaderAliasTypeColumnTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(LiveCatalogReaderAliasTypeColumnTests);

    protected override string Ddl => """
        CREATE TYPE dbo.PersonName FROM NVARCHAR(50) NULL;
        GO
        CREATE TYPE dbo.YesNo FROM BIT NOT NULL;
        GO
        CREATE TYPE dbo.Amount FROM DECIMAL(12, 3) NULL;
        GO
        CREATE TABLE dbo.Holder (
            Id INT NOT NULL,
            Nm dbo.PersonName,
            Flg dbo.YesNo,
            Amt dbo.Amount,
            Plain NVARCHAR(50) NULL
        );
        """;

    private async Task<SilentScan.Core.Catalog.CatalogTable> ReadHolderAsync()
    {
        var catalog = await new LiveCatalogReader(Options.BuildConnectionString(DatabaseName)).ReadAsync();
        return catalog.Find("dbo.Holder")!;
    }

    [Fact]
    public async Task ReadAsync_AliasTypedColumns_ResolveToTheirBaseTypeLikeAnInlineDeclaration()
    {
        var holder = await ReadHolderAsync();
        var byName = holder.Columns.ToDictionary(c => c.Name);

        Assert.Equal(SqlTypeCategory.NVarChar, byName["Nm"].Type?.Category);
        Assert.Equal(byName["Plain"].Type, byName["Nm"].Type);
        Assert.Equal(SqlTypeCategory.Bit, byName["Flg"].Type?.Category);
        Assert.Equal(SqlTypeCategory.Decimal, byName["Amt"].Type?.Category);
        Assert.Equal(12, byName["Amt"].Type?.Precision);
        Assert.Equal(3, byName["Amt"].Type?.Scale);
    }
}
