using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed class OrderByExpressionLiteralVerifier : RuleVerifier<ForcedParameterizationFinding>
{
    public override string RuleId => FindingRuleIds.ForcedParameterizationOrderByExpressionLiteralRuleId;

    protected override Perturbation Describe(ForcedParameterizationFinding finding) =>
        LiteralParameterizationProbe.Describe(finding.LiteralSourceText);
}
