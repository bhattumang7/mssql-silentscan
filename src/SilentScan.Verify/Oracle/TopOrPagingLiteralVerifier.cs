using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed class TopOrPagingLiteralVerifier : RuleVerifier<ForcedParameterizationFinding>
{
    public override string RuleId => FindingRuleIds.ForcedParameterizationTopOrPagingLiteralRuleId;

    protected override Perturbation Describe(ForcedParameterizationFinding finding) =>
        LiteralParameterizationProbe.Describe(finding.LiteralSourceText);
}
