using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed class FloatEqualityVerifier : RuleVerifier<FloatEqualityFinding>
{
    public override string RuleId => FindingRuleIds.FloatEqualityRuleId;

    protected override Perturbation Describe(FloatEqualityFinding finding)
    {
        if (ColumnAddress.TryParse(finding.TableQualifiedName, finding.ColumnName) is not { } address)
        {
            return Perturbation.NotApplicable($"'{finding.TableQualifiedName}.{finding.ColumnName}' is not a plain persisted table column the probe can address.");
        }

        return new Perturbation(
            [address.Touched],
            Difference.RowCountChanged,
            static (_, _, _) => Task.CompletedTask,
            (connection, transaction, cancellationToken) => ColumnAddress.ExecuteAsync(
                connection, transaction, $"UPDATE {address.Table} SET {address.Column} = {address.Column} + {address.Column} * 1.0E-6;", cancellationToken),
            static _ => false);
    }
}
