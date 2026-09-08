using Microsoft.Data.SqlClient;
using SilentScan.Core.Predicates;
using SilentScan.Verify;
using SilentScan.Verify.Deployment;

namespace SilentScan.Live.Sweep;

public static class SweepRunner
{
    public static async Task<IReadOnlyList<SweepResult>> RunAsync(
        IReadOnlyList<RuleExampleCase> cases,
        SqlServerOptions options,
        int maxParallelism = 6,
        CancellationToken cancellationToken = default)
    {
        var results = new SweepResult[cases.Count];
        using var throttle = new SemaphoreSlim(maxParallelism);

        var work = cases.Select(async (@case, index) =>
        {
            await throttle.WaitAsync(cancellationToken);
            try
            {
                results[index] = await RunOneAsync(@case, options, cancellationToken);
            }
            catch (Exception ex) when (ex is SqlException or InvalidOperationException)
            {
                results[index] = new SweepResult(@case, SweepOutcome.HarnessError, ex.Message, []);
            }
            finally
            {
                throttle.Release();
            }
        });

        await Task.WhenAll(work);
        return results;
    }

    private static async Task<SweepResult> RunOneAsync(RuleExampleCase @case, SqlServerOptions options, CancellationToken cancellationToken)
    {
        if (!@case.IsSelfContained)
        {
            return new SweepResult(@case, SweepOutcome.NotSelfContained, "example has no deployable object definition and no extractable prelude", []);
        }

        if (ServerScopedStatementGuard.ContainsServerScopedDdl(@case.DeployableSql))
        {
            return new SweepResult(@case, SweepOutcome.ServerScopedSkipped, "example deploys server-scoped DDL (e.g. ON ALL SERVER trigger, login, audit) that would outlive the disposable sweep database; not deployed", []);
        }

        var databaseName = $"SilentScanSweep_{Guid.NewGuid():N}";
        var provisioner = new DatabaseProvisioner(options);
        await provisioner.CreateFreshAsync(databaseName, cancellationToken: cancellationToken);
        try
        {
            var wrapped = BareStatementHarness.WrapBareStatementsInProcedures(@case.DeployableSql);
            try
            {
                await new ScriptDeployer(options).DeployAsync(wrapped, databaseName, cancellationToken);
            }
            catch (Exception ex) when (ex is SqlException or InvalidOperationException)
            {
                return new SweepResult(@case, SweepOutcome.DeployFailed, ex.Message, []);
            }

            var scanResult = await LiveScanRunner.RunAsync(
                options.BuildConnectionString(databaseName),
                minimumConfidence: FindingConfidence.Low,
                cancellationToken: cancellationToken);
            var firedRuleIds = ScanReportRuleIds.Extract(scanResult.Report);
            var ownRuleFired = firedRuleIds.Any(id => IsSameRule(id, @case.RuleId));
            var foreignRuleIds = firedRuleIds.Where(id => !IsSameRule(id, @case.RuleId)).ToList();

            var outcome = @case.Variant switch
            {
                RuleExampleVariant.Noncompliant => ownRuleFired ? SweepOutcome.Confirmed : SweepOutcome.OwnRuleSilent,
                RuleExampleVariant.Compliant => ownRuleFired ? SweepOutcome.OwnRuleFiredOnCompliant : SweepOutcome.Confirmed,
                _ => throw new ArgumentOutOfRangeException(nameof(@case)),
            };

            return new SweepResult(@case, outcome, null, foreignRuleIds);
        }
        finally
        {
            await provisioner.DropIfExistsAsync(databaseName, cancellationToken);
        }
    }

    private static bool IsSameRule(string firedRuleId, string caseRuleId) =>
        firedRuleId == caseRuleId
        || firedRuleId == $"{caseRuleId}/medium-confidence"
        || firedRuleId == $"{caseRuleId}/low-confidence";
}
