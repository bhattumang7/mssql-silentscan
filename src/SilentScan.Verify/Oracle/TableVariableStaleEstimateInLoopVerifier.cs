using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed class TableVariableStaleEstimateInLoopVerifier : RuleVerifier<QueryAntiPatternFinding>
{
    public override string RuleId => FindingRuleIds.QueryAntiPatternTableVariableStaleEstimateInLoopRuleId;

    protected override Perturbation Describe(QueryAntiPatternFinding finding) => RecompileHintProbe.Describe(finding.DetailText, finding.SourcePath);
}
