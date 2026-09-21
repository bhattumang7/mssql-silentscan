using System.Globalization;
using Microsoft.Data.SqlClient;
using SilentScan.Core.Predicates;
using SilentScan.Live;
using SilentScan.Verify;
using SilentScan.Verify.Deployment;

namespace SilentScan.Live.Sweep;

public static class BehaviorSweepRunner
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
        if (@case.BehaviorProof is not { } proof)
        {
            return new SweepResult(@case, SweepOutcome.BehaviorMissing, "example has no executable behavior proof", []);
        }

        if (!@case.IsSelfContained)
        {
            return new SweepResult(@case, SweepOutcome.NotSelfContained, "example has no deployable object definition and no extractable prelude", []);
        }

        if (ServerScopedStatementGuard.ContainsServerScopedDdl(@case.DeployableSql))
        {
            return new SweepResult(@case, SweepOutcome.ServerScopedSkipped, "example deploys server-scoped DDL", []);
        }

        var databaseName = $"SilentScanBehavior_{Guid.NewGuid():N}";
        var provisioner = new DatabaseProvisioner(options);
        await provisioner.CreateFreshAsync(databaseName, cancellationToken: cancellationToken);
        try
        {
            var deployer = new ScriptDeployer(options);
            await deployer.DeployAsync(BareStatementHarness.WrapBareStatementsInProcedures(@case.DeployableSql), databaseName, cancellationToken);
            await deployer.DeployAsync(proof.SetupSql, databaseName, cancellationToken);

            var scanResult = await LiveScanRunner.RunAsync(
                options.BuildConnectionString(databaseName),
                minimumConfidence: FindingConfidence.Low,
                cancellationToken: cancellationToken);
            var firedRuleIds = ScanReportRuleIds.Extract(scanResult.Report);
            var ownRuleFired = firedRuleIds.Any(id => IsSameRule(id, @case.RuleId));
            var foreignRuleIds = firedRuleIds.Where(id => !IsSameRule(id, @case.RuleId)).ToList();

            if ((@case.Variant == RuleExampleVariant.Noncompliant && !ownRuleFired)
                || (@case.Variant == RuleExampleVariant.Compliant && ownRuleFired))
            {
                var outcome = @case.Variant == RuleExampleVariant.Noncompliant
                    ? SweepOutcome.OwnRuleSilent
                    : SweepOutcome.OwnRuleFiredOnCompliant;
                return new SweepResult(@case, outcome, null, foreignRuleIds);
            }

            await using var connection = new SqlConnection(options.BuildConnectionString(databaseName));
            await connection.OpenAsync(cancellationToken);
            await using var command = new SqlCommand(proof.QuerySql, connection);
            var actual = await command.ExecuteScalarAsync(cancellationToken);
            var actualScalar = actual is null or DBNull
                ? null
                : Convert.ToString(actual, CultureInfo.InvariantCulture);

            return actualScalar == proof.ExpectedScalar
                ? new SweepResult(@case, SweepOutcome.Confirmed, null, foreignRuleIds)
                : new SweepResult(@case, SweepOutcome.BehaviorMismatch, $"expected scalar '{proof.ExpectedScalar ?? "NULL"}' but got '{actualScalar ?? "NULL"}'", foreignRuleIds);
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
