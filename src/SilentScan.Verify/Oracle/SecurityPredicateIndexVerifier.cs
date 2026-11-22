using System.Text.RegularExpressions;
using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed partial class SecurityPredicateIndexVerifier : RuleVerifier<SecurityPredicateIndexFinding>
{
    public override string RuleId => FindingRuleIds.SecurityPredicateIndexRuleId;

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex PlainIdentifier();

    protected override Perturbation Describe(SecurityPredicateIndexFinding finding)
    {
        if (TableAddress.TryParse(finding.TableQualifiedName) is not { } table
            || finding.FilteredColumns.Count == 0
            || !PlainIdentifier().IsMatch(finding.FilteredColumns[0]))
        {
            return Perturbation.NotApplicable("The finding does not name a plain table and column the probe can address.");
        }

        return new Perturbation(
            [table.Touched],
            Difference.PlanChanged,
            static (_, _, _) => Task.FromResult(0),
            (connection, transaction, cancellationToken) => TableAddress.ExecuteAsync(
                connection, transaction, $"CREATE INDEX silentscan_probe_index ON {table.Table} ([{finding.FilteredColumns[0]}]);", cancellationToken),
            static _ => false,
            touchesReadAfterCondition: true);
    }
}
