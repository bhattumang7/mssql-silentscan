using System.Text.RegularExpressions;
using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed partial class TemporalHistoryIndexGapVerifier : RuleVerifier<TemporalTableHistoryIndexGapFinding>
{
    public override string RuleId => FindingRuleIds.TemporalTableHistoryIndexGapRuleId;

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex PlainIdentifier();

    protected override Perturbation Describe(TemporalTableHistoryIndexGapFinding finding)
    {
        if (TableAddress.TryParse(finding.HistoryTableQualifiedName) is not { } history
            || finding.KeyColumns.Count == 0
            || finding.KeyColumns.Any(column => !PlainIdentifier().IsMatch(column)))
        {
            return Perturbation.NotApplicable("The finding does not name a plain history table and key columns the probe can address.");
        }

        var keys = string.Join(", ", finding.KeyColumns.Select(column => $"[{column}]"));
        return new Perturbation(
            [history.Touched],
            Difference.PlanChanged,
            static (_, _, _) => Task.FromResult(0),
            (connection, transaction, cancellationToken) => TableAddress.ExecuteAsync(
                connection, transaction, $"CREATE INDEX silentscan_probe_index ON {history.Table} ({keys});", cancellationToken),
            static _ => false,
            touchesReadAfterCondition: true);
    }
}
