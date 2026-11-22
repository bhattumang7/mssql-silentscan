using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed class GroupByExpressionLiteralVerifier : RuleVerifier<ForcedParameterizationFinding>
{
    public override string RuleId => FindingRuleIds.ForcedParameterizationGroupByExpressionLiteralRuleId;

    protected override Perturbation Describe(ForcedParameterizationFinding finding) =>
        LiteralParameterizationProbe.Describe(finding.LiteralSourceText);
}
