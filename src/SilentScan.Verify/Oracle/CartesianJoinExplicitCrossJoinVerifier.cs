using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed class CartesianJoinExplicitCrossJoinVerifier : RuleVerifier<CartesianJoinFinding>
{
    public override string RuleId => FindingRuleIds.CartesianJoinExplicitCrossJoinRuleId;

    protected override Perturbation Describe(CartesianJoinFinding finding) => ExtraRowProbe.Describe(finding.SecondTableQualifiedName);
}
