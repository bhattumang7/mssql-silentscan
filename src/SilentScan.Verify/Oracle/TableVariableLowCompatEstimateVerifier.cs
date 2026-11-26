using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed class TableVariableLowCompatEstimateVerifier : RuleVerifier<QueryAntiPatternFinding>
{
    public override string RuleId => FindingRuleIds.QueryAntiPatternTableVariableLowCompatEstimateRuleId;

    protected override Perturbation Describe(QueryAntiPatternFinding finding) => RecompileHintProbe.Describe(finding.DetailText, finding.SourcePath);
}
