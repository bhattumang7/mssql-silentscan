using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed class ProcCallArgumentMismatchVerifier : RuleVerifier<ProcCallArgumentMismatchFinding>
{
    public override string RuleId => FindingRuleIds.ProcCallArgumentMismatchRuleId;

    protected override Perturbation Describe(ProcCallArgumentMismatchFinding finding) =>
        finding.IsOutputWriteback
            ? Perturbation.NotApplicable("The probe handles input string and binary parameters only.")
            : CalleeParameterWidening.Describe(finding.CalleeQualifiedName, finding.FormalParameterName, finding.FormalParameterTypeDisplay);
}
