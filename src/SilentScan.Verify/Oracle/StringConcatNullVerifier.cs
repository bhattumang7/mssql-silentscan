using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed class StringConcatNullVerifier : RuleVerifier<StringConcatNullFinding>
{
    public override string RuleId => FindingRuleIds.StringConcatNullRuleId;

    protected override Perturbation Describe(StringConcatNullFinding finding)
    {
        if (ColumnAddress.TryParse(finding.TableQualifiedName, finding.ColumnName) is not { } address)
        {
            return Perturbation.NotApplicable($"'{finding.TableQualifiedName}.{finding.ColumnName}' is not a plain persisted table column the probe can address.");
        }

        return new Perturbation(
            [address.Touched],
            Difference.NullAppeared,
            (connection, transaction, cancellationToken) => ColumnAddress.ExecuteAsync(connection, transaction, $"UPDATE {address.Table} SET {address.Column} = '' WHERE {address.Column} IS NULL;", cancellationToken),
            (connection, transaction, cancellationToken) => ColumnAddress.ExecuteAsync(connection, transaction, $"UPDATE {address.Table} SET {address.Column} = NULL;", cancellationToken),
            address.NamesColumn);
    }
}
