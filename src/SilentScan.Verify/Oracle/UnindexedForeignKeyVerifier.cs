using System.Text.RegularExpressions;
using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed partial class UnindexedForeignKeyVerifier : RuleVerifier<IndexDesignFinding>
{
    public override string RuleId => FindingRuleIds.IndexDesignUnindexedForeignKeyRuleId;

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex PlainIdentifier();

    protected override Perturbation Describe(IndexDesignFinding finding)
    {
        if (TableAddress.TryParse(finding.TableQualifiedName) is not { } table
            || finding.ColumnNames is not { Count: > 0 } columns
            || columns.Any(column => !PlainIdentifier().IsMatch(column)))
        {
            return Perturbation.NotApplicable("The finding does not name a plain table and foreign key columns the probe can address.");
        }

        var keys = string.Join(", ", columns.Select(column => $"[{column}]"));
        return new Perturbation(
            [],
            Difference.PlanChanged,
            static (_, _, _) => Task.FromResult(0),
            (connection, transaction, cancellationToken) => TableAddress.ExecuteAsync(
                connection, transaction, $"CREATE INDEX silentscan_probe_index ON {table.Table} ({keys});", cancellationToken),
            static _ => false);
    }
}
