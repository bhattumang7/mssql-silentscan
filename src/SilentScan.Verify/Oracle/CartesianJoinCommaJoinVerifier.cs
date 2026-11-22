using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed class CartesianJoinCommaJoinVerifier : RuleVerifier<CartesianJoinFinding>
{
    public override string RuleId => FindingRuleIds.CartesianJoinCommaJoinRuleId;

    protected override Perturbation Describe(CartesianJoinFinding finding) => ExtraRowProbe.Describe(finding.SecondTableQualifiedName);
}
