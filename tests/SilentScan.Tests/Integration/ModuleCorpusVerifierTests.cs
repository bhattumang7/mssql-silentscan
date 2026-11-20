using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;
using SilentScan.Live;
using SilentScan.Verify;
using SilentScan.Verify.Catalog;
using SilentScan.Verify.Deployment;
using SilentScan.Verify.Oracle;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
[Trait("Rule", "silentscan/verdict/scan-forced")]
public sealed class ModuleCorpusVerifierTests : IAsyncLifetime
{
    private const string DatabaseName = "SilentScanModuleCorpusTest";

    private readonly SqlServerOptions _options = SqlServerOptions.LocalDocker;
    private readonly DatabaseProvisioner _provisioner;

    public ModuleCorpusVerifierTests()
    {
        _provisioner = new DatabaseProvisioner(_options);
    }

    public async Task InitializeAsync()
    {
        await _provisioner.CreateFreshAsync(DatabaseName);
        await new ScriptDeployer(_options).DeployAsync(
            """
            CREATE TABLE dbo.Orders (OrderId INT NOT NULL PRIMARY KEY, OrderCode VARCHAR(20) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL);
            GO
            CREATE INDEX IX_Orders_OrderCode ON dbo.Orders(OrderCode);
            GO
            CREATE VIEW dbo.vwOrdersByCode AS SELECT OrderId FROM dbo.Orders WHERE OrderCode = N'x';
            GO
            CREATE FUNCTION dbo.fnOrdersByCode (@code NVARCHAR(20)) RETURNS TABLE AS RETURN SELECT OrderId FROM dbo.Orders WHERE OrderCode = @code;
            GO
            CREATE PROCEDURE dbo.FindByCode @code NVARCHAR(20) AS
            BEGIN
                SELECT OrderId FROM dbo.Orders WHERE OrderCode = @code;
                SELECT OrderId FROM dbo.Orders WHERE OrderCode = 'x';
            END;
            GO
            """,
            DatabaseName);
    }

    public async Task DisposeAsync() =>
        await _provisioner.DropIfExistsAsync(DatabaseName);

    [Fact]
    public async Task VerifyAsync_ScannerFindingOnAMismatchedStatement_AgreesWithTheEnginePlanOfTheSameModule()
    {
        var connectionString = _options.BuildConnectionString(DatabaseName);
        var scan = await LiveScanRunner.RunAsync(connectionString);
        var findings = scan.Report.FindingsByRuleId.Values
            .SelectMany(f => f)
            .OfType<TypedPredicateFinding>()
            .Where(f => f.Verdict is Verdict.ScanForced or Verdict.RangeSeek)
            .ToList();
        var module = (await new LiveModuleReader(connectionString).ReadAsync())
            .Modules.Single(m => string.Equals(m.QualifiedName, "dbo.FindByCode", StringComparison.OrdinalIgnoreCase));

        var rows = await new ModuleCorpusVerifier(_options).VerifyAsync(DatabaseName, module, findings);

        var row = Assert.Single(rows);
        Assert.Equal(ModuleComparisonOutcome.Agree, row.Outcome);
        Assert.Equal("OrderCode", row.Column);
        Assert.Equal(Verdict.ScanForced, row.Finding!.Verdict);
    }

    [Fact]
    public async Task VerifyAsync_ModuleKindThatCannotBePlanned_IsNotDecidedWithTheKindNamed()
    {
        var connectionString = _options.BuildConnectionString(DatabaseName);
        var module = (await new LiveModuleReader(connectionString).ReadAsync())
            .Modules.Single(m => string.Equals(m.QualifiedName, "dbo.FindByCode", StringComparison.OrdinalIgnoreCase))
            with { ObjectTypeCode = "TR" };
        var scan = await LiveScanRunner.RunAsync(connectionString);
        var findings = scan.Report.FindingsByRuleId.Values.SelectMany(f => f).OfType<TypedPredicateFinding>()
            .Where(f => f.Verdict == Verdict.ScanForced && string.Equals(f.SourcePath, "dbo.FindByCode", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var rows = await new ModuleCorpusVerifier(_options).VerifyAsync(DatabaseName, module, findings);

        var row = Assert.Single(rows);
        Assert.Equal(ModuleComparisonOutcome.NotDecided, row.Outcome);
        Assert.Equal(ModuleNotDecidedReason.ModuleKindNotPlannable, row.Reason);
        Assert.Equal("object type TR", row.Detail);
    }

    [Theory]
    [InlineData("dbo.vwOrdersByCode")]
    [InlineData("dbo.fnOrdersByCode")]
    public async Task VerifyAsync_ViewOrInlineFunctionWithAMismatchedPredicate_AgreesWithThePlanOfTheWholeModule(string moduleName)
    {
        var connectionString = _options.BuildConnectionString(DatabaseName);
        var scan = await LiveScanRunner.RunAsync(connectionString);
        var module = (await new LiveModuleReader(connectionString).ReadAsync())
            .Modules.Single(m => string.Equals(m.QualifiedName, moduleName, StringComparison.OrdinalIgnoreCase));
        var findings = scan.Report.FindingsByRuleId.Values
            .SelectMany(f => f)
            .OfType<TypedPredicateFinding>()
            .Where(f => f.Verdict is Verdict.ScanForced or Verdict.RangeSeek && string.Equals(f.SourcePath, moduleName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var rows = await new ModuleCorpusVerifier(_options).VerifyAsync(DatabaseName, module, findings);

        var row = Assert.Single(rows);
        Assert.Equal(ModuleComparisonOutcome.Agree, row.Outcome);
        Assert.Equal("OrderCode", row.Column);
    }
}
