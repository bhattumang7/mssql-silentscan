using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed class NotEqualsNullComparisonVerifier : RuleVerifier<DeprecatedSyntaxFinding>
{
    public override string RuleId => FindingRuleIds.DeprecatedSyntaxNotEqualsNullComparisonRuleId;

    protected override Perturbation Describe(DeprecatedSyntaxFinding finding) =>
        finding.DetailText.StartsWith("\"<> NULL\"", StringComparison.Ordinal)
            ? new Perturbation(
                [],
                Difference.ResultChanged,
                (connection, transaction, cancellationToken) => TableAddress.ExecuteAsync(connection, transaction, "SET ANSI_NULLS OFF;", cancellationToken),
                (connection, transaction, cancellationToken) => TableAddress.ExecuteAsync(connection, transaction, "SET ANSI_NULLS ON;", cancellationToken),
                static _ => false)
            : Perturbation.NotApplicable("The finding is not the comparison this rule describes.");
}
