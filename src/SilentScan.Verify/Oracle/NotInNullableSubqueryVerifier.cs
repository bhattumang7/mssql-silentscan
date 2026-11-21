using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed class NotInNullableSubqueryVerifier : RuleVerifier<NotInNullableSubqueryFinding>
{
    public override string RuleId => FindingRuleIds.NotInNullableSubqueryRuleId;

    protected override Perturbation Describe(NotInNullableSubqueryFinding finding)
    {
        if (ColumnAddress.TryParse(finding.SubqueryTableQualifiedName, finding.SubqueryColumnName) is not { } address)
        {
            return Perturbation.NotApplicable($"'{finding.SubqueryTableQualifiedName}.{finding.SubqueryColumnName}' is not a plain persisted table column the probe can address.");
        }

        return new Perturbation(
            [address.Touched],
            Difference.ResultEmptied,
            (connection, transaction, cancellationToken) => ColumnAddress.ExecuteAsync(connection, transaction, $"DELETE FROM {address.Table} WHERE {address.Column} IS NULL;", cancellationToken),
            (connection, transaction, cancellationToken) => ColumnAddress.ExecuteAsync(connection, transaction, $"INSERT INTO {address.Table} ({address.Column}) VALUES (NULL);", cancellationToken),
            address.NamesColumn);
    }
}
