using System.Reflection;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using SilentScan.Core.Predicates;
using SilentScan.Core.Reporting;
using SilentScan.Core.Reporting.Sarif;
using SilentScan.Verify;
using SilentScan.Verify.Deployment;
using SilentScan.Verify.Oracle;

namespace SilentScan.Tests.Support;

public sealed record OracleCase(string Setup, string Probe);

public abstract class RuleOracle
{
    protected abstract OracleCase Fires { get; }

    protected abstract OracleCase Silent { get; }

    protected abstract IFinding ClaimedOnSilent { get; }

    private string RuleId
    {
        get
        {
            var ids = GetType().GetCustomAttributesData()
                .Where(data => data.AttributeType == typeof(TraitAttribute)
                    && (string)data.ConstructorArguments[0].Value! == "Rule")
                .Select(data => (string)data.ConstructorArguments[1].Value!)
                .ToList();
            return ids.Count == 1
                ? ids[0]
                : throw new InvalidOperationException($"{GetType().Name} must declare exactly one Rule trait.");
        }
    }

    private IRuleVerifier Verifier =>
        RuleVerifierRegistry.For(RuleId)
        ?? throw new InvalidOperationException($"{RuleId} has no registered RuleVerifier in SilentScan.Verify.");

    [Fact]
    public async Task FiringCase_ScannerFlagsItAndEveryFindingIsConfirmedByTheEngine()
    {
        var verifier = Verifier;
        var report = await ScanAsync(Fires);
        var findings = FindingsOf(report, verifier);

        Assert.True(FlaggedInSarif(report), $"{RuleId}: the scanner did not report this rule on the firing case.");
        Assert.NotEmpty(findings);

        var results = await VerifyAgainstEngineAsync(Fires, verifier, findings);
        Assert.All(results, result => Assert.True(
            result.Verdict == EngineVerdict.Confirmed,
            $"{RuleId}: the engine did not confirm a scanner finding ({result.Verdict}): {result.Detail}"));
    }

    [Fact]
    public async Task SilentCase_ScannerStaysSilentAndTheEngineRefutesTheClaim()
    {
        var verifier = Verifier;
        var report = await ScanAsync(Silent);

        Assert.False(FlaggedInSarif(report), $"{RuleId}: the scanner reported this rule on the silent case.");
        Assert.Empty(FindingsOf(report, verifier));

        var results = await VerifyAgainstEngineAsync(Silent, verifier, [ClaimedOnSilent]);
        var result = Assert.Single(results);
        Assert.True(
            result.Verdict == EngineVerdict.Refuted,
            $"{RuleId}: the engine did not refute the claim on the silent case ({result.Verdict}): {result.Detail}");
    }

    private static async Task<ScanReport> ScanAsync(OracleCase oracleCase)
    {
        var sql = string.IsNullOrWhiteSpace(oracleCase.Setup)
            ? oracleCase.Probe
            : oracleCase.Setup + "\nGO\n" + oracleCase.Probe;
        return await EngineAuthoritativeScan.ScanAsync(sql);
    }

    private static List<IFinding> FindingsOf(ScanReport report, IRuleVerifier verifier) =>
        [.. report.FindingsByRuleId.Values.SelectMany(findings => findings).Where(verifier.Judges)];

    private bool FlaggedInSarif(ScanReport report)
    {
        using var document = JsonDocument.Parse(SarifReportWriter.Write(report));
        var ruleId = RuleId;
        return document.RootElement.GetProperty("runs")[0].GetProperty("results").EnumerateArray()
            .Any(result => string.Equals(result.GetProperty("ruleId").GetString(), ruleId, StringComparison.Ordinal));
    }

    private static async Task<List<EngineVerification>> VerifyAgainstEngineAsync(
        OracleCase oracleCase, IRuleVerifier verifier, IReadOnlyList<IFinding> findings)
    {
        var options = SqlServerOptions.LocalDocker;
        var databaseName = $"SilentScanRuleOracle_{Guid.NewGuid():N}";
        var provisioner = new DatabaseProvisioner(options);
        await provisioner.CreateFreshAsync(databaseName);
        try
        {
            if (!string.IsNullOrWhiteSpace(oracleCase.Setup))
            {
                await new ScriptDeployer(options).DeployAsync(oracleCase.Setup, databaseName);
            }

            var results = new List<EngineVerification>();
            foreach (var finding in findings)
            {
                results.Add(await verifier.VerifyAsync(options, databaseName, finding, oracleCase.Probe));
            }

            return results;
        }
        finally
        {
            SqlConnection.ClearAllPools();
            await provisioner.DropIfExistsAsync(databaseName);
        }
    }
}
