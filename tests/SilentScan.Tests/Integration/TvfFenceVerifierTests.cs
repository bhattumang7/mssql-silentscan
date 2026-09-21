using SilentScan.Core.Predicates;
using SilentScan.Verify;
using SilentScan.Verify.Deployment;
using SilentScan.Verify.Oracle;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
[Trait("Rule", "silentscan/tvf-fence/correlated-apply")]
public sealed class TvfFenceVerifierTests : IAsyncLifetime
{
    private const string DatabaseName = "SilentScanTvfFenceVerifierTest";

    private readonly SqlServerOptions _options = SqlServerOptions.LocalDocker;
    private readonly DatabaseProvisioner _provisioner;
    private readonly TvfFenceVerifier _verifier;

    public TvfFenceVerifierTests()
    {
        _provisioner = new DatabaseProvisioner(_options);
        _verifier = new TvfFenceVerifier(_options);
    }

    public async Task InitializeAsync()
    {
        await _provisioner.CreateFreshAsync(DatabaseName);

        const string ddl = """
            CREATE FUNCTION dbo.fn_Fence(@Id INT)
            RETURNS @T TABLE (Id INT)
            AS
            BEGIN
                INSERT INTO @T (Id) SELECT @Id;
                RETURN;
            END;
            GO
            CREATE FUNCTION dbo.itvf_NotAFence(@Id INT)
            RETURNS TABLE
            AS
            RETURN (SELECT @Id AS Id);
            GO
            """;
        await new ScriptDeployer(_options).DeployAsync(ddl, DatabaseName);
    }

    public async Task DisposeAsync() =>
        await _provisioner.DropIfExistsAsync(DatabaseName);

    private static TvfFenceFinding FunctionFinding(TvfFenceFindingKind kind, string functionQualifiedName) => new(
        kind, functionQualifiedName, functionQualifiedName, Core.Catalog.TableValuedFunctionKind.MultiStatement, "file.sql", 1, 1);

    [Fact]
    public async Task VerifyAsync_MultiStatementTvfReference_ConfirmsTableValuedFunctionOperator()
    {
        var finding = FunctionFinding(TvfFenceFindingKind.CorrelatedApply, "dbo.fn_Fence");

        var result = await _verifier.VerifyAsync(DatabaseName, finding);

        Assert.Equal(TvfFenceOutcome.Confirmed, result.Outcome);
    }

    [Fact]
    public async Task VerifyAsync_MislabeledInlineTvf_IsNotConfirmed()
    {

        var finding = FunctionFinding(TvfFenceFindingKind.CorrelatedApply, "dbo.itvf_NotAFence");

        var result = await _verifier.VerifyAsync(DatabaseName, finding);

        Assert.Equal(TvfFenceOutcome.NotConfirmed, result.Outcome);
    }

    [Fact]
    public async Task VerifyAsync_CorrelatedApplyKind_StillConfirmsViaDummyArguments()
    {

        var finding = FunctionFinding(TvfFenceFindingKind.CorrelatedApply, "dbo.fn_Fence");

        var result = await _verifier.VerifyAsync(DatabaseName, finding);

        Assert.Equal(TvfFenceOutcome.Confirmed, result.Outcome);
    }

    [Fact]
    public async Task VerifyAsync_UnknownFunctionName_IsNotProbeable()
    {
        var finding = FunctionFinding(TvfFenceFindingKind.CorrelatedApply, "dbo.fn_DoesNotExist");

        var result = await _verifier.VerifyAsync(DatabaseName, finding);

        Assert.Equal(TvfFenceOutcome.NotProbeable, result.Outcome);
    }
}
