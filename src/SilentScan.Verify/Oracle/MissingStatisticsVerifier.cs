using System.Text.RegularExpressions;
using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed partial class MissingStatisticsVerifier : RuleVerifier<MissingStatisticsFinding>
{
    public override string RuleId => FindingRuleIds.MissingStatisticsRuleId;

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex PlainIdentifier();

    protected override Perturbation Describe(MissingStatisticsFinding finding)
    {
        if (TableAddress.TryParse(finding.TableQualifiedName) is not { } table
            || !PlainIdentifier().IsMatch(finding.ColumnName))
        {
            return Perturbation.NotApplicable("The finding does not name a plain table and column the probe can address.");
        }

        return new Perturbation(
            [table.Touched],
            Difference.EstimatesChanged,
            static (_, _, _) => Task.FromResult(0),
            (connection, transaction, cancellationToken) => TableAddress.ExecuteAsync(
                connection, transaction, $"CREATE STATISTICS silentscan_probe_statistic ON {table.Table} ([{finding.ColumnName}]) WITH FULLSCAN;", cancellationToken),
            static _ => false);
    }
}
