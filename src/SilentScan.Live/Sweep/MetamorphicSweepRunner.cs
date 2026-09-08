using Microsoft.Data.SqlClient;
using SilentScan.Core.Predicates;
using SilentScan.Verify;
using SilentScan.Verify.Deployment;

namespace SilentScan.Live.Sweep;

public static class MetamorphicSweepRunner
{
    public static async Task<IReadOnlyList<MetamorphicResult>> RunAsync(
        IReadOnlyList<RuleExampleCase> cases,
        SqlServerOptions options,
        int maxParallelism = 6,
        CancellationToken cancellationToken = default)
    {
        var baseCases = cases
            .Where(c => c.Variant == RuleExampleVariant.Noncompliant && c.IsSelfContained)
            .Where(c => !ServerScopedStatementGuard.ContainsServerScopedDdl(c.DeployableSql))
            .ToList();

        var results = new List<MetamorphicResult>[baseCases.Count];
        using var throttle = new SemaphoreSlim(maxParallelism);

        var work = baseCases.Select(async (baseCase, index) =>
        {
            await throttle.WaitAsync(cancellationToken);
            try
            {
                results[index] = await RunOneAsync(baseCase, options, cancellationToken);
            }
            finally
            {
                throttle.Release();
            }
        });

        await Task.WhenAll(work);
        return [.. results.SelectMany(r => r)];
    }

    private static async Task<List<MetamorphicResult>> RunOneAsync(RuleExampleCase baseCase, SqlServerOptions options, CancellationToken cancellationToken)
    {
        var results = new List<MetamorphicResult>();

        var (baselineDeployed, baselineFiredRuleIds) = await DeployAndScanAsync(baseCase.DeployableSql, options, cancellationToken);
        if (!baselineDeployed)
        {
            return results;
        }

        foreach (var mutation in MetamorphicMutator.Mutate(baseCase.DeployableSql))
        {
            if (ServerScopedStatementGuard.ContainsServerScopedDdl(mutation.MutatedSql))
            {
                continue;
            }

            var (mutatedDeployed, mutatedFiredRuleIds) = await DeployAndScanAsync(mutation.MutatedSql, options, cancellationToken);
            if (!mutatedDeployed)
            {
                continue;
            }

            var outcome = baselineFiredRuleIds.SetEquals(mutatedFiredRuleIds) ? MetamorphicOutcome.Matched : MetamorphicOutcome.Mismatched;
            results.Add(new MetamorphicResult(baseCase, mutation.Name, outcome, baselineFiredRuleIds, mutatedFiredRuleIds));
        }

        return results;
    }

    private static async Task<(bool Deployed, IReadOnlySet<string> FiredRuleIds)> DeployAndScanAsync(string sql, SqlServerOptions options, CancellationToken cancellationToken)
    {
        var databaseName = $"SilentScanMetamorphic_{Guid.NewGuid():N}";
        var provisioner = new DatabaseProvisioner(options);
        await provisioner.CreateFreshAsync(databaseName, cancellationToken: cancellationToken);
        try
        {
            var wrapped = BareStatementHarness.WrapBareStatementsInProcedures(sql);
            try
            {
                await new ScriptDeployer(options).DeployAsync(wrapped, databaseName, cancellationToken);
            }
            catch (Exception ex) when (ex is SqlException or InvalidOperationException)
            {
                return (false, new HashSet<string>());
            }

            var scanResult = await LiveScanRunner.RunAsync(
                options.BuildConnectionString(databaseName),
                minimumConfidence: FindingConfidence.Low,
                cancellationToken: cancellationToken);
            return (true, ScanReportRuleIds.Extract(scanResult.Report));
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException)
        {
            return (false, new HashSet<string>());
        }
        finally
        {
            await provisioner.DropIfExistsAsync(databaseName, cancellationToken);
        }
    }
}
