using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;

namespace SilentScan.Tests.Predicates;

public sealed class SecurityScannerTests
{
    private static IReadOnlyList<SecurityFinding> Scan(string sql)
    {
        var result = SqlScriptParser.ParseText("test.sql", sql);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        return SecurityScanner.Scan(result);
    }

    [Fact]
    public void CallToExternalRestEndpoint_Fires()
    {
        var findings = Scan("""
            CREATE PROCEDURE dbo.P AS
            BEGIN
                EXEC sp_invoke_external_rest_endpoint @url = 'https://example.com/webhook';
            END
            """);

        var finding = Assert.Single(findings, f => f.Kind == SecurityFindingKind.ExternalRestEndpointCall);
        Assert.Equal(FindingConfidence.High, finding.Confidence);
    }

    [Fact]
    public void CallToUnrelatedProcedure_NeverFiresExternalRestEndpoint()
    {
        var findings = Scan("""
            CREATE PROCEDURE dbo.P AS
            BEGIN
                EXEC dbo.SomeOtherProcedure @Value = 1;
            END
            """);

        Assert.DoesNotContain(findings, f => f.Kind == SecurityFindingKind.ExternalRestEndpointCall);
    }
}
