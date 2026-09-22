using SilentScan.Core.Catalog;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;
using SilentScan.Verify;
using SilentScan.Verify.Deployment;
using SilentScan.Verify.Oracle;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
[Trait("Rule", "silentscan/tier1/column-arithmetic")]
public sealed class ColumnArithmeticComputedColumnOracleTests : IAsyncLifetime
{
    private const string DatabaseName = "SilentScanArithCompColOracleTest";

    private readonly SqlServerOptions _options = SqlServerOptions.LocalDocker;
    private readonly DatabaseProvisioner _provisioner;

    private const string Ddl = """
        CREATE TABLE dbo.OrderLines
        (
            OrderLineId INT NOT NULL,
            Price DECIMAL(10,2) NOT NULL,
            Tax DECIMAL(10,2) NOT NULL,
            Total AS (Price + Tax) PERSISTED
        );
        GO
        CREATE INDEX IX_OrderLines_Total ON dbo.OrderLines(Total);
        GO
        INSERT INTO dbo.OrderLines(OrderLineId, Price, Tax)
        SELECT TOP (5000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)),
               ROW_NUMBER() OVER (ORDER BY (SELECT NULL)),
               1
        FROM sys.all_objects a CROSS JOIN sys.all_objects b;
        GO
        UPDATE STATISTICS dbo.OrderLines WITH FULLSCAN;
        GO
        CREATE PROCEDURE dbo.ProbePriceAndTaxMatchingComputedColumn @x DECIMAL(10,2) AS
        BEGIN
            SELECT OrderLineId FROM dbo.OrderLines WHERE Price + Tax = @x;
        END
        GO
        """;

    public ColumnArithmeticComputedColumnOracleTests()
    {
        _provisioner = new DatabaseProvisioner(_options);
    }

    public async Task InitializeAsync()
    {
        await _provisioner.CreateFreshAsync(DatabaseName);
        await new ScriptDeployer(_options).DeployAsync(Ddl, DatabaseName);
    }

    public async Task DisposeAsync() =>
        await _provisioner.DropIfExistsAsync(DatabaseName);

    [Fact]
    public async Task PriceAndTaxMatchingIndexedComputedColumn_Seeks()
    {
        var planXml = await new PlanXmlCapture(_options).CaptureAsync(
            DatabaseName, "EXEC dbo.ProbePriceAndTaxMatchingComputedColumn @x = 2;");

        Assert.True(IndexAccessDetector.HasIndexSeek(planXml, "IX_OrderLines_Total"));

        var result = SqlScriptParser.ParseText(
            "test.sql",
            $"{Ddl}\nGO\nSELECT OrderLineId FROM dbo.OrderLines WHERE Price + Tax = 2;");
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));

        var catalog = CatalogBuilder.Build([result]);
        var lineage = LineageResolver.Resolve(catalog, [result]);
        var findings = NonSargablePredicateScanner.Scan(result, catalog, lineage);

        Assert.DoesNotContain(findings, f => f.Kind == SargabilityFindingKind.ColumnArithmetic);
    }
}
