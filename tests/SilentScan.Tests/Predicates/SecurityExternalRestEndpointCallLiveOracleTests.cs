using Microsoft.Data.SqlClient;
using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;
using SilentScan.Verify;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", "silentscan/security/external-rest-endpoint-call")]
public sealed class SecurityExternalRestEndpointCallLiveOracleTests
{
    [Fact]
    public async Task RealCatalog_SpInvokeExternalRestEndpoint_IsAGenuineReachableSystemProcedure_UnlikeAMadeUpName()
    {
        await using var connection = new SqlConnection(SqlServerOptions.LocalDocker.BuildConnectionString());
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM sys.all_objects WHERE name = 'sp_invoke_external_rest_endpoint';";
        var realCount = (int)(await command.ExecuteScalarAsync())!;

        await using var madeUpCommand = connection.CreateCommand();
        madeUpCommand.CommandText =
            "SELECT COUNT(*) FROM sys.all_objects WHERE name = 'sp_invoke_external_rest_endpoint_made_up';";
        var madeUpCount = (int)(await madeUpCommand.ExecuteScalarAsync())!;

        Assert.Equal(1, realCount);
        Assert.Equal(0, madeUpCount);
    }

    [Fact]
    public async Task LiveDeployment_CallToExternalRestEndpoint_Fires()
    {
        var report = await EngineAuthoritativeScan.ScanAsync(
            """
            EXEC sp_invoke_external_rest_endpoint @url = 'https://example.com/webhook';
            """,
            minimumConfidence: FindingConfidence.Low);

        var finding = Assert.Single(report.Find<SecurityFinding>("SecurityScanner"), f => f.Kind == SecurityFindingKind.ExternalRestEndpointCall);
        Assert.Equal(FindingConfidence.High, finding.Confidence);
    }

    [Fact]
    public async Task LiveDeployment_CallToUnrelatedProcedure_NeverFires()
    {
        var report = await EngineAuthoritativeScan.ScanAsync(
            """
            EXEC sp_who;
            """,
            minimumConfidence: FindingConfidence.Low);

        Assert.DoesNotContain(report.Find<SecurityFinding>("SecurityScanner"), f => f.Kind == SecurityFindingKind.ExternalRestEndpointCall);
    }
}
