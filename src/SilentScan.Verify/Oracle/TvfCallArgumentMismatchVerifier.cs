using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed class TvfCallArgumentMismatchVerifier : RuleVerifier<TvfCallArgumentMismatchFinding>
{
    public override string RuleId => FindingRuleIds.TvfCallArgumentMismatchRuleId;

    protected override Perturbation Describe(TvfCallArgumentMismatchFinding finding) =>
        CalleeParameterWidening.Describe(finding.CalleeQualifiedName, finding.FormalParameterName, finding.FormalParameterTypeDisplay);
}
