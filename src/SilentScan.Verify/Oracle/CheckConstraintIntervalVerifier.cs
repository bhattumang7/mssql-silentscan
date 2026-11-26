using System.Text.RegularExpressions;
using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed partial class CheckConstraintIntervalVerifier : RuleVerifier<CheckConstraintPredicateContradictionFinding>
{
    public override string RuleId => FindingRuleIds.CheckConstraintPredicateContradictionIntervalRuleId;

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex PlainIdentifier();

    protected override Perturbation Describe(CheckConstraintPredicateContradictionFinding finding)
    {
        if (!string.Equals(finding.RuleId, RuleId, StringComparison.Ordinal))
        {
            return Perturbation.NotApplicable("The finding is not about a CHECK constraint interval.");
        }

        if (TableAddress.TryParse(finding.TableQualifiedName) is not { } table
            || finding.ConstraintName is not { } constraint
            || !PlainIdentifier().IsMatch(constraint))
        {
            return Perturbation.NotApplicable("The finding does not name a plain table and constraint the probe can address.");
        }

        return new Perturbation(
            [table.Touched],
            Difference.PlanChanged,
            static (_, _, _) => Task.FromResult(0),
            (connection, transaction, cancellationToken) => TableAddress.ExecuteAsync(
                connection, transaction, $"ALTER TABLE {table.Table} DROP CONSTRAINT [{constraint}];", cancellationToken),
            static _ => false,
            options: new PerturbationOptions(TouchesReadAfterCondition: true, PlanCompiledInModule: true));
    }
}
