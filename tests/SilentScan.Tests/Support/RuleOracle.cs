using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
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

public sealed record SilentCase(OracleCase Case, string FiringPartner, Func<IFinding, IFinding>? Differing = null);

public sealed record EngineTarget(SqlServerOptions Options, int? CompatibilityLevel)
{
    public string Label => $"{Options.Host}:{Options.Port} compatibility {(CompatibilityLevel?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "default")}";
}

public sealed record StatementForm(string Name, Func<string, bool> Applies, Func<OracleCase, OracleCase> Apply);

public static partial class StatementForms
{
    public const string Procedure = "procedure";

    public const string View = "view";

    public const string CommonTableExpression = "common table expression";

    public const string DerivedTable = "derived table";

    public const string BracketedNames = "aliases and bracketed names";

    public const string DynamicSql = "dynamic SQL";

    private const string ProcedureName = "dbo.SilentScanCaseProcedure";

    private const string ViewName = "dbo.SilentScanCaseView";

    private const string Line = "\n";

    private const string Break = "\nGO\n";

    public static IReadOnlyList<StatementForm> All { get; } =
    [
        new(Procedure, static _ => true, static c => c with
        {
            Setup = c.Setup + Break + "CREATE PROCEDURE " + ProcedureName + " AS BEGIN" + Line + c.Statement + Line + "END" + Break,
            Statement = "EXEC " + ProcedureName + ";",
        }),
        new(View, IsQuery, static c => c with
        {
            Setup = c.Setup + Break + "CREATE VIEW " + ViewName + " AS" + Line + Body(c.Statement) + Break,
            Statement = "SELECT * FROM " + ViewName + ";",
        }),
        new(CommonTableExpression, IsQuery, static c => c with { Statement = "WITH SilentScanCase AS (" + Line + Body(c.Statement) + Line + ") SELECT * FROM SilentScanCase;" }),
        new(DerivedTable, IsQuery, static c => c with { Statement = "SELECT * FROM (" + Line + Body(c.Statement) + Line + ") AS SilentScanCase;" }),
        new(BracketedNames, static s => SchemaQualifiedName().IsMatch(s), static c => c with { Statement = BracketAndAlias(c.Statement) }),
        new(DynamicSql, static _ => true, static c => c with { Statement = "EXEC sp_executesql N'" + c.Statement.Replace("'", "''", StringComparison.Ordinal) + "';" }),
    ];

    private static bool IsQuery(string statement) => statement.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase);

    private static string Body(string statement) => statement.TrimEnd().TrimEnd(';');

    private static string BracketAndAlias(string statement) =>
        IsQuery(statement)
            ? FromSchemaQualifiedName().Replace(statement, "FROM [dbo].[$1] AS [Aliased]")
            : SchemaQualifiedName().Replace(statement, "[dbo].[$1]");

    [GeneratedRegex(@"\bdbo\.([A-Za-z_][A-Za-z0-9_]*)")]
    private static partial Regex SchemaQualifiedName();

    [GeneratedRegex(@"\bFROM\s+dbo\.([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.IgnoreCase)]
    private static partial Regex FromSchemaQualifiedName();
}

public abstract class RuleOracle
{
    private const string UnrelatedStatement = "SELECT 1;";

    private static readonly int[] CommonCompatibilityLevels = [110, 120, 130, 140, 150, 160];

    private static readonly EngineTarget[] AllTargets =
    [
        .. WithLevels(SqlServerOptions.LocalDocker, []),
        .. WithLevels(SqlServerOptions.LocalDockerLatest, [170]),
    ];

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, ScanReport> _scans = [];

    protected virtual IReadOnlyList<EngineTarget> Targets => AllTargets;

    protected abstract IReadOnlyList<OracleCase> FiringCases { get; }

    protected abstract IReadOnlyList<SilentCase> SilentCases { get; }

    protected abstract IReadOnlyList<Func<IFinding, IFinding>> ClaimMutations { get; }

    protected virtual IReadOnlyList<RuleCondition> Conditions =>
        RuleCatalog.BaseRules.Single(rule => string.Equals(rule.Id, RuleId, StringComparison.Ordinal)).DeclaredConditions;

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
        RequireWellFormed("firing", cases);
        RequirePartnersResolve(cases, SilentCases);
        Assert.True(ClaimMutations.Count > 0, $"{RuleId} declares no claim mutations, so the claim-swap control has nothing to run.");

        var excluded = ExcludedForms();
        var failures = new List<string>();
        var executed = 0;
        foreach (var oracleCase in cases)
        {
            var partners = SilentCases.Where(s => string.Equals(s.FiringPartner, oracleCase.Name, StringComparison.Ordinal)).Select(s => s.Case).ToList();
            var perTarget = await Task.WhenAll(Targets.Select(target => FiringOnTargetAsync(oracleCase, partners, target, excluded)));
            failures.AddRange(perTarget.SelectMany(f => f));

            executed++;
        }

        Assert.True(executed == cases.Count, $"{RuleId}: {cases.Count - executed} declared firing case(s) did not execute.");
        Assert.True(failures.Count == 0, $"{RuleId}: {failures.Count} firing check(s) failed:\n{string.Join('\n', failures)}");
    }

    [Fact]
    public async Task SilentCases_ScannerStaysSilentAndTheEngineRefutesEachClaim()
    {
        var cases = SilentCases;
        RequireWellFormed("silent", [.. cases.Select(c => c.Case)]);
        RequirePartnersResolve(FiringCases, cases);

        var excluded = ExcludedForms();
        var failures = new List<string>();
        var executed = 0;
        var refutedByPerturbation = 0;
        foreach (var silentCase in cases)
        {
            var partner = FiringCases.Single(f => string.Equals(f.Name, silentCase.FiringPartner, StringComparison.Ordinal));
            var perTarget = await Task.WhenAll(Targets.Select(target => SilentOnTargetAsync(silentCase, partner, target, excluded)));
            failures.AddRange(perTarget.SelectMany(r => r.Failures));
            refutedByPerturbation += perTarget.Sum(r => r.RefutedByPerturbation);

            executed++;
        }

        Assert.True(executed == cases.Count, $"{RuleId}: {cases.Count - executed} declared silent case(s) did not execute.");
        Assert.True(failures.Count == 0, $"{RuleId}: {failures.Count} silent check(s) failed:\n{string.Join('\n', failures)}");
        Assert.True(refutedByPerturbation > 0, $"{RuleId}: no silent case was refuted by a perturbation that took effect, so the perturbation was never shown able to come out negative.");
    }

    private async Task<List<string>> FiringOnTargetAsync(
        OracleCase oracleCase, IReadOnlyList<OracleCase> partners, EngineTarget target, IReadOnlyList<StatementForm> excluded)
    {
        var failures = new List<string>();
        Collect(failures, $"{oracleCase.Name} on {target.Label}", await CheckFiringAsync(oracleCase, partners, target, true));

        foreach (var form in JudgedForms(oracleCase.Statement, excluded))
        {
            Collect(failures, $"{oracleCase.Name} as {form.Name} on {target.Label}", await CheckFiringAsync(form.Apply(oracleCase), [], target, false));
        }

        foreach (var form in excluded.Where(f => f.Applies(oracleCase.Statement)))
        {
            Collect(failures, $"{oracleCase.Name} as excluded {form.Name} on {target.Label}", await CheckScannerSilentAsync(form.Apply(oracleCase), target, form.Name));
        }

        return failures;
    }

    private async Task<SilentTargetResult> SilentOnTargetAsync(
        SilentCase silentCase, OracleCase partner, EngineTarget target, IReadOnlyList<StatementForm> excluded)
    {
        var failures = new List<string>();
        var outcome = await CheckSilentAsync(silentCase, partner, target, null);
        Collect(failures, $"{silentCase.Case.Name} on {target.Label}", outcome.Failure);
        var refuted = outcome.Verification is { Verdict: EngineVerdict.Refuted, EngineRefusal: false } ? 1 : 0;

        foreach (var form in JudgedForms(silentCase.Case.Statement, excluded).Where(f => f.Applies(partner.Statement)))
        {
            var shaped = await CheckSilentAsync(silentCase, partner, target, form);
            Collect(failures, $"{silentCase.Case.Name} as {form.Name} on {target.Label}", shaped.Failure);
        }

        return new SilentTargetResult(failures, refuted);
    }

    private static IEnumerable<EngineTarget> WithLevels(SqlServerOptions options, int[] extraLevels) =>
        new int?[] { null }.Concat(CommonCompatibilityLevels.Concat(extraLevels).Select(level => (int?)level)).Select(level => new EngineTarget(options, level));

    private static void Collect(List<string> failures, string label, string? failure)
    {
        if (failure is not null)
        {
            failures.Add($"[{label}] {failure}");
        }
    }

    private List<StatementForm> ExcludedForms()
    {
        var names = Conditions.Where(c => c.Form is not null).Select(c => c.Form!).Distinct(StringComparer.Ordinal).ToList();
        var unknown = names.Where(name => StatementForms.All.All(f => !string.Equals(f.Name, name, StringComparison.Ordinal))).ToList();
        Assert.True(unknown.Count == 0, $"{RuleId} excludes unknown statement form(s): {string.Join(", ", unknown)}.");
        return [.. StatementForms.All.Where(f => names.Contains(f.Name, StringComparer.Ordinal))];
    }

    private static IEnumerable<StatementForm> JudgedForms(string statement, IReadOnlyList<StatementForm> excluded) =>
        StatementForms.All.Where(f => f.Applies(statement) && !excluded.Contains(f));

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

    private async Task<string?> CheckFiringAsync(OracleCase oracleCase, IReadOnlyList<OracleCase> partners, EngineTarget target, bool swapControls)
    {
        var verifier = Verifier;
        var report = await ScanAsync(oracleCase, target);
        var findings = FindingsOf(report, verifier);

        if (!FlaggedInSarif(report))
        {
            return "the scanner did not report this rule.";
        }

        if (findings.Count == 0)
        {
            return "the scanner reported the rule in SARIF but produced no finding for the verifier to judge.";
        }

        var probes = new List<Probe> { new("judged statement", false, oracleCase.Statement, findings) };
        if (swapControls)
        {
            probes.AddRange(partners.Select(p => new Probe($"swapped for silent partner '{p.Name}'", false, p.Statement, findings)));
            probes.Add(new Probe("swapped for a statement that touches nothing", false, UnrelatedStatement, findings));
            probes.AddRange(ClaimMutations.Select((mutation, index) =>
                new Probe($"claim mutation {index + 1} applied", true, oracleCase.Statement, [.. findings.Select(mutation)])));
        }

        var results = await VerifyAgainstEngineAsync(oracleCase.Setup, verifier, probes, target);
        var judged = results[0].FirstOrDefault(r => r.Verdict != EngineVerdict.Confirmed);
        if (judged is not null)
        {
            return $"the engine did not confirm a finding on {target.Label} ({judged.Verdict}): {judged.Detail}";
        }

        for (var i = 1; i < probes.Count; i++)
        {
            if (results[i].Any(r => r.Verdict == EngineVerdict.Confirmed))
            {
                return probes[i].IsClaimSwap
                    ? $"the verifier confirmed the finding after its claim was changed ({probes[i].Label}); it is not judging the claim."
                    : $"the verifier confirmed the finding when its statement was {probes[i].Label}; it is not judging the statement.";
            }
        }

        return null;
    }

    private async Task<string?> CheckScannerSilentAsync(OracleCase oracleCase, EngineTarget target, string formName)
    {
        var report = await ScanAsync(oracleCase, target);
        return FlaggedInSarif(report) || FindingsOf(report, Verifier).Count > 0
            ? $"the scanner fires in the {formName} form, so excluding that form is false."
            : null;
    }

    private async Task<SilentOutcome> CheckSilentAsync(SilentCase silentCase, OracleCase partner, EngineTarget target, StatementForm? form)
    {
        var verifier = Verifier;
        var silent = form is null ? silentCase.Case : form.Apply(silentCase.Case);
        var firing = form is null ? partner : form.Apply(partner);

        var silentReport = await ScanAsync(silent, target);
        if (FlaggedInSarif(silentReport) || FindingsOf(silentReport, verifier).Count > 0)
        {
            return new SilentOutcome("the scanner reported this rule on a silent case.", null);
        }

        var partnerFindings = FindingsOf(await ScanAsync(firing, target), verifier);
        if (partnerFindings.Count == 0)
        {
            return new SilentOutcome($"the firing partner '{partner.Name}' produced no real finding to derive the claim from.", null);
        }

        var claim = silentCase.Differing is { } differing ? differing(partnerFindings[0]) : partnerFindings[0];
        var probe = new Probe("silent statement", false, silent.Statement, [claim]);
        var verification = Assert.Single(Assert.Single(await VerifyAgainstEngineAsync(silent.Setup, verifier, [probe], target)));
        return verification.Verdict == EngineVerdict.Refuted
            ? new SilentOutcome(null, verification)
            : new SilentOutcome($"the engine did not refute the claim on {target.Label} ({verification.Verdict}): {verification.Detail}", verification);
    }

    private async Task<ScanReport> ScanAsync(OracleCase oracleCase, EngineTarget target)
    {
        var sql = string.IsNullOrWhiteSpace(oracleCase.Setup)
            ? oracleCase.Statement
            : oracleCase.Setup + "\nGO\n" + oracleCase.Statement;
        var key = target.Label + "\n" + sql;
        if (!_scans.TryGetValue(key, out var report))
        {
            report = await EngineAuthoritativeScan.ScanOnAsync(sql, target.Options, target.CompatibilityLevel);
            _scans[key] = report;
        }

        return report;
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
        string setup, IRuleVerifier verifier, IReadOnlyList<Probe> probes, EngineTarget target)
    {
        var databaseName = $"SilentScanRuleOracle_{Guid.NewGuid():N}";
        var provisioner = new DatabaseProvisioner(target.Options);
        await provisioner.CreateFreshAsync(databaseName);
        try
        {
            if (target.CompatibilityLevel is { } level)
            {
                await provisioner.SetCompatibilityLevelAsync(databaseName, level);
            }

            if (!string.IsNullOrWhiteSpace(setup))
            {
                await new ScriptDeployer(target.Options).DeployAsync(setup, databaseName);
            }

            var results = new List<List<EngineVerification>>();
            foreach (var probe in probes)
            {
                var perFinding = new List<EngineVerification>();
                foreach (var finding in probe.Findings)
                {
                    perFinding.Add(await verifier.VerifyAsync(target.Options, databaseName, finding, probe.Statement));
                }

                results.Add(perFinding);
            }

            Assert.True(
                results.Count == probes.Count && results.Zip(probes).All(pair => pair.First.Count == pair.Second.Findings.Count),
                "Some verifications did not execute.");
            return results;
        }
        finally
        {
            SqlConnection.ClearAllPools();
            await provisioner.DropIfExistsAsync(databaseName);
        }
    }

    private sealed record Probe(string Label, bool IsClaimSwap, string Statement, IReadOnlyList<IFinding> Findings);

    private sealed record SilentTargetResult(List<string> Failures, int RefutedByPerturbation);

    private sealed record SilentOutcome(string? Failure, EngineVerification? Verification);
}
