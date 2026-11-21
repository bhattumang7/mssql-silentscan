using System.Reflection;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;
using SilentScan.Core.Reporting;
using SilentScan.Core.Reporting.Sarif;
using SilentScan.Verify;
using SilentScan.Verify.Deployment;
using SilentScan.Verify.Oracle;

namespace SilentScan.Tests.Support;

public sealed record OracleCase(string Name, string Setup, string Statement, string? Proves = null)
{
    public string Entry => Proves ?? Name;
}

public sealed record SilentCase(OracleCase Case, IFinding Claim, string FiringPartner);

public abstract class RuleOracle
{
    private const string UnrelatedStatement = "SELECT 1;";

    protected abstract IReadOnlyList<OracleCase> FiringCases { get; }

    protected abstract IReadOnlyList<SilentCase> SilentCases { get; }

    protected virtual IRuleVerifier Verifier =>
        RuleVerifierRegistry.For(RuleId)
        ?? throw new InvalidOperationException($"{RuleId} has no registered RuleVerifier in SilentScan.Verify.");

    public string DeclaredRuleId => RuleId;

    public IReadOnlyList<OracleCase> DeclaredFiringCases => FiringCases;

    public IReadOnlyList<SilentCase> DeclaredSilentCases => SilentCases;

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

    [Fact]
    public async Task FiringCases_ScannerFlagsEachAndTheEngineConfirmsEveryFinding()
    {
        var cases = FiringCases;
        var silent = SilentCases;
        RequireWellFormed("firing", cases);
        RequirePartnersResolve(cases, silent);

        var failures = new List<string>();
        var executed = 0;
        foreach (var oracleCase in cases)
        {
            var partners = silent.Where(s => string.Equals(s.FiringPartner, oracleCase.Name, StringComparison.Ordinal)).Select(s => s.Case).ToList();
            if (await CheckFiringAsync(oracleCase, partners) is { } failure)
            {
                failures.Add($"[{oracleCase.Name}] {failure}");
            }

            executed++;
        }

        Assert.True(executed == cases.Count, $"{RuleId}: {cases.Count - executed} declared firing case(s) did not execute.");
        Assert.True(failures.Count == 0, $"{RuleId}: {failures.Count} firing case(s) failed:\n{string.Join('\n', failures)}");
    }

    [Fact]
    public async Task SilentCases_ScannerStaysSilentAndTheEngineRefutesEachClaim()
    {
        var cases = SilentCases;
        RequireWellFormed("silent", [.. cases.Select(c => c.Case)]);
        RequirePartnersResolve(FiringCases, cases);

        var failures = new List<string>();
        var executed = 0;
        foreach (var silentCase in cases)
        {
            if (await CheckSilentAsync(silentCase) is { } failure)
            {
                failures.Add($"[{silentCase.Case.Name}] {failure}");
            }

            executed++;
        }

        Assert.True(executed == cases.Count, $"{RuleId}: {cases.Count - executed} declared silent case(s) did not execute.");
        Assert.True(failures.Count == 0, $"{RuleId}: {failures.Count} silent case(s) failed:\n{string.Join('\n', failures)}");
    }

    private static void RequireWellFormed(string side, IReadOnlyList<OracleCase> cases)
    {
        Assert.True(cases.Count > 0, $"A rule needs at least one {side} case.");

        var duplicates = cases.GroupBy(c => c.Name, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(duplicates.Count == 0, $"Duplicate {side} case name(s): {string.Join(", ", duplicates)}.");

        foreach (var oracleCase in cases)
        {
            Assert.True(!string.IsNullOrWhiteSpace(oracleCase.Statement), $"[{oracleCase.Name}] the statement is blank; nothing would be judged.");

            var parsed = SqlScriptParser.ParseText("case.sql", oracleCase.Statement);
            var statementCount = parsed.Fragment is TSqlScript script ? script.Batches.Sum(b => b.Statements.Count) : 0;
            Assert.True(
                !parsed.HasErrors && statementCount == 1,
                $"[{oracleCase.Name}] a case must hold exactly one judged statement; found {statementCount}{(parsed.HasErrors ? " with parse errors" : string.Empty)}.");
        }
    }

    private static void RequirePartnersResolve(IReadOnlyList<OracleCase> firing, IReadOnlyList<SilentCase> silent)
    {
        foreach (var silentCase in silent)
        {
            var partner = firing.FirstOrDefault(f => string.Equals(f.Name, silentCase.FiringPartner, StringComparison.Ordinal));
            Assert.True(partner is not null, $"[{silentCase.Case.Name}] names firing partner '{silentCase.FiringPartner}', which does not exist.");
            Assert.True(
                string.Equals(partner!.Setup, silentCase.Case.Setup, StringComparison.Ordinal),
                $"[{silentCase.Case.Name}] must share its setup with firing partner '{silentCase.FiringPartner}' so the pair differs only in the statement.");
        }
    }

    private async Task<string?> CheckFiringAsync(OracleCase oracleCase, IReadOnlyList<OracleCase> partners)
    {
        var verifier = Verifier;
        var report = await ScanAsync(oracleCase);
        var findings = FindingsOf(report, verifier);

        if (!FlaggedInSarif(report))
        {
            return "the scanner did not report this rule.";
        }

        if (findings.Count == 0)
        {
            return "the scanner reported the rule in SARIF but produced no finding for the verifier to judge.";
        }

        var statements = new List<(string Label, string Statement)> { ("judged statement", oracleCase.Statement) };
        statements.AddRange(partners.Select(p => ($"swapped for silent partner '{p.Name}'", p.Statement)));
        statements.Add(("swapped for a statement that touches nothing", UnrelatedStatement));

        var results = await VerifyAgainstEngineAsync(oracleCase.Setup, verifier, findings, [.. statements.Select(s => s.Statement)]);
        for (var i = 0; i < statements.Count; i++)
        {
            var failure = i == 0
                ? results[i].FirstOrDefault(r => r.Verdict != EngineVerdict.Confirmed) is { } unconfirmed
                    ? $"the engine did not confirm a finding ({unconfirmed.Verdict}): {unconfirmed.Detail}"
                    : null
                : results[i].FirstOrDefault(r => r.Verdict == EngineVerdict.Confirmed) is not null
                    ? $"the verifier confirmed the finding when its statement was {statements[i].Label}; it is not judging the statement."
                    : null;
            if (failure is not null)
            {
                return failure;
            }
        }

        return null;
    }

    private async Task<string?> CheckSilentAsync(SilentCase silentCase)
    {
        var verifier = Verifier;
        var report = await ScanAsync(silentCase.Case);

        if (FlaggedInSarif(report) || FindingsOf(report, verifier).Count > 0)
        {
            return "the scanner reported this rule on a silent case.";
        }

        var result = Assert.Single(Assert.Single(await VerifyAgainstEngineAsync(silentCase.Case.Setup, verifier, [silentCase.Claim], [silentCase.Case.Statement])));
        return result.Verdict == EngineVerdict.Refuted ? null : $"the engine did not refute the claim ({result.Verdict}): {result.Detail}";
    }

    private static async Task<ScanReport> ScanAsync(OracleCase oracleCase)
    {
        var sql = string.IsNullOrWhiteSpace(oracleCase.Setup)
            ? oracleCase.Statement
            : oracleCase.Setup + "\nGO\n" + oracleCase.Statement;
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

    private static async Task<List<List<EngineVerification>>> VerifyAgainstEngineAsync(
        string setup, IRuleVerifier verifier, IReadOnlyList<IFinding> findings, IReadOnlyList<string> statements)
    {
        var options = SqlServerOptions.LocalDocker;
        var databaseName = $"SilentScanRuleOracle_{Guid.NewGuid():N}";
        var provisioner = new DatabaseProvisioner(options);
        await provisioner.CreateFreshAsync(databaseName);
        try
        {
            if (!string.IsNullOrWhiteSpace(setup))
            {
                await new ScriptDeployer(options).DeployAsync(setup, databaseName);
            }

            var results = new List<List<EngineVerification>>();
            foreach (var statement in statements)
            {
                var perStatement = new List<EngineVerification>();
                foreach (var finding in findings)
                {
                    perStatement.Add(await verifier.VerifyAsync(options, databaseName, finding, statement));
                }

                results.Add(perStatement);
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
