using System.Text.RegularExpressions;
using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed partial class UntrustedCheckConstraintVerifier : RuleVerifier<UntrustedConstraintFinding>
{
    public override string RuleId => FindingRuleIds.UntrustedCheckConstraintRuleId;

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex PlainIdentifier();

    protected override Perturbation Describe(UntrustedConstraintFinding finding)
    {
        if (TableAddress.TryParse(finding.TableQualifiedName) is not { } table
            || !PlainIdentifier().IsMatch(finding.ConstraintName))
        {
            return Perturbation.NotApplicable("The finding does not name a plain table and constraint the probe can address.");
        }

        return new Perturbation(
            [],
            Difference.PlanChanged,
            static (_, _, _) => Task.FromResult(0),
            (connection, transaction, cancellationToken) => TableAddress.ExecuteAsync(
                connection,
                transaction,
                $"ALTER TABLE {table.Table} WITH CHECK CHECK CONSTRAINT [{finding.ConstraintName}];",
                cancellationToken),
            static _ => false);
    }
}
