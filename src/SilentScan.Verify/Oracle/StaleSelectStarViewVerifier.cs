using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed class StaleSelectStarViewVerifier : RuleVerifier<StaleSelectStarViewFinding>
{
    public override string RuleId => FindingRuleIds.StaleSelectStarViewRuleId;

    protected override Perturbation Describe(StaleSelectStarViewFinding finding)
    {
        if (TableAddress.TryParse(finding.ViewQualifiedName) is not { } view)
        {
            return Perturbation.NotApplicable("The finding does not name a plain view the probe can address.");
        }

        return new Perturbation(
            [],
            Difference.ResultChanged,
            static (_, _, _) => Task.FromResult(0),
            (connection, transaction, cancellationToken) => TableAddress.ExecuteAsync(
                connection, transaction, $"EXEC sp_refreshview N'{view.Table.Replace("'", "''", StringComparison.Ordinal)}';", cancellationToken),
            static _ => false);
    }
}
