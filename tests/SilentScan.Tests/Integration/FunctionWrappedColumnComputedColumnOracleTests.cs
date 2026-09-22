using SilentScan.Core.Catalog;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;
using SilentScan.Verify;
using SilentScan.Verify.Deployment;
using SilentScan.Verify.Oracle;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
[Trait("Rule", "silentscan/tier1/function-wrapped-column")]
public sealed class FunctionWrappedColumnComputedColumnOracleTests : IAsyncLifetime
{
    private const string DatabaseName = "SilentScanFuncWrapCompColOracleTest";

    private readonly SqlServerOptions _options = SqlServerOptions.LocalDocker;
    private readonly DatabaseProvisioner _provisioner;

    private const string Ddl = """
        CREATE TABLE dbo.Contacts
        (
            Phone VARCHAR(20) NOT NULL,
            PhoneDigitsOnly AS REPLACE(Phone, '-', '') PERSISTED
        );
        GO
        CREATE INDEX IX_Contacts_PhoneDigitsOnly ON dbo.Contacts(PhoneDigitsOnly);
        GO
        INSERT INTO dbo.Contacts(Phone)
        SELECT TOP (5000) '555-' + RIGHT('0000' + CAST(ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS VARCHAR(10)), 4)
        FROM sys.all_objects a CROSS JOIN sys.all_objects b;
        GO
        UPDATE STATISTICS dbo.Contacts WITH FULLSCAN;
        GO
        CREATE PROCEDURE dbo.ProbeReplaceMatchingComputedColumn @x VARCHAR(20) AS
        BEGIN
            SELECT Phone FROM dbo.Contacts WHERE REPLACE(Phone, '-', '') = @x;
        END
        GO
        """;

    public FunctionWrappedColumnComputedColumnOracleTests()
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
    public async Task ReplaceMatchingIndexedComputedColumn_Seeks()
    {
        var planXml = await new PlanXmlCapture(_options).CaptureAsync(
            DatabaseName, "EXEC dbo.ProbeReplaceMatchingComputedColumn @x = '5550001';");

        Assert.True(IndexAccessDetector.HasIndexSeek(planXml, "IX_Contacts_PhoneDigitsOnly"));

        var result = SqlScriptParser.ParseText(
            "test.sql",
            $"{Ddl}\nGO\nSELECT Phone FROM dbo.Contacts WHERE REPLACE(Phone, '-', '') = '5550001';");
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));

        var catalog = CatalogBuilder.Build([result]);
        var lineage = LineageResolver.Resolve(catalog, [result]);
        var findings = NonSargablePredicateScanner.Scan(result, catalog, lineage);

        Assert.DoesNotContain(findings, f => f.Kind == SargabilityFindingKind.FunctionWrappedColumn);
    }
}
