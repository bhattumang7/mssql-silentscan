using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed class ConvertStyleCodeLiteralVerifier : RuleVerifier<ForcedParameterizationFinding>
{
    public override string RuleId => FindingRuleIds.ForcedParameterizationConvertStyleCodeLiteralRuleId;

    protected override Perturbation Describe(ForcedParameterizationFinding finding) =>
        LiteralParameterizationProbe.Describe(finding.LiteralSourceText);
}
