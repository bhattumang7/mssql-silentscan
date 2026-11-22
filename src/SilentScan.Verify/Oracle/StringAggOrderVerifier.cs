using System.Text.RegularExpressions;
using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed partial class StringAggOrderVerifier : RuleVerifier<StringAggMissingOrderFinding>
{
    public override string RuleId => FindingRuleIds.StringAggMissingOrderRuleId;

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex PlainIdentifier();

    protected override Perturbation Describe(StringAggMissingOrderFinding finding)
    {
        if (TableAddress.TryParse(finding.TableQualifiedName) is not { } table
            || finding.ColumnName is not { } column
            || !PlainIdentifier().IsMatch(column))
        {
            return Perturbation.NotApplicable("The finding does not name a plain table and column the probe can address.");
        }

        return new Perturbation(
            [table.Touched],
            Difference.ResultChanged,
            static (_, _, _) => Task.FromResult(0),
            (connection, transaction, cancellationToken) => TableAddress.ExecuteAsync(
                connection, transaction, $"CREATE INDEX silentscan_probe_index ON {table.Table} ([{column}]);", cancellationToken),
            static _ => false,
            touchesReadAfterCondition: true);
    }
}
