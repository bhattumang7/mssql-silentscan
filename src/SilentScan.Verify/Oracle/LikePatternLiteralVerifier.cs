using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed class LikePatternLiteralVerifier : RuleVerifier<ForcedParameterizationFinding>
{
    public override string RuleId => FindingRuleIds.ForcedParameterizationLikePatternLiteralRuleId;

    protected override Perturbation Describe(ForcedParameterizationFinding finding) =>
        LiteralParameterizationProbe.Describe(finding.LiteralSourceText);
}
