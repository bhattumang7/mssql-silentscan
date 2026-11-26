using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed class StringAggOrderVerifier : RuleVerifier<StringAggMissingOrderFinding>
{
    public override string RuleId => FindingRuleIds.StringAggMissingOrderRuleId;

    protected override Perturbation Describe(StringAggMissingOrderFinding finding) =>
        ScratchIndexOrderProbe.Describe(finding.TableQualifiedName, finding.ColumnName);
}
