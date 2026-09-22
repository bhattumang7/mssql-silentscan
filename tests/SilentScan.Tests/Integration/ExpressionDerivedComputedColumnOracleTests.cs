using SilentScan.Core.Catalog;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;
using SilentScan.Verify;
using SilentScan.Verify.Deployment;
using SilentScan.Verify.Oracle;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
[Trait("Rule", "silentscan/lineage/expression-derived-column")]
public sealed class ExpressionDerivedComputedColumnOracleTests : IAsyncLifetime
{
    private const string DatabaseName = "SilentScanExprDerivCompColOracleTest";
    private const string IndexName = "IX_Orders_CustomerIdStr";

    private readonly SqlServerOptions _options = SqlServerOptions.LocalDocker;
    private readonly DatabaseProvisioner _provisioner;

    private const string Ddl = """
        CREATE TABLE dbo.Orders
        (
            OrderId INT NOT NULL PRIMARY KEY,
            CustomerId INT NOT NULL,
            CustomerIdStr AS CAST(CustomerId AS VARCHAR(20)) PERSISTED
        );
        GO
        CREATE INDEX IX_Orders_CustomerIdStr ON dbo.Orders(CustomerIdStr);
        GO
        CREATE VIEW dbo.vw_OrdersStr AS
        SELECT OrderId, CAST(CustomerId AS VARCHAR(20)) AS CustomerIdStr
        FROM dbo.Orders;
        GO
        """;

    public ExpressionDerivedComputedColumnOracleTests()
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
    public async Task ViewCastMatchingIndexedComputedColumn_Seeks()
    {
        var planXml = await new PlanXmlCapture(_options).CaptureAsync(
            DatabaseName, "SELECT OrderId FROM dbo.vw_OrdersStr WHERE CustomerIdStr = '5';");

        Assert.True(IndexAccessDetector.HasIndexSeek(planXml, IndexName));

        var result = SqlScriptParser.ParseText(
            "test.sql",
            $"{Ddl}\nGO\nSELECT OrderId FROM dbo.vw_OrdersStr WHERE CustomerIdStr = '5';");
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));

        var catalog = CatalogBuilder.Build([result]);
        var lineage = LineageResolver.Resolve(catalog, [result]);
        var extracted = TypedPredicateExtractor.Extract(result, catalog, lineage);

        Assert.Empty(extracted.ExpressionDerivedFindings);
    }
}
