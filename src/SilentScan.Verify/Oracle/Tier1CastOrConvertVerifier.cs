using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed class Tier1CastOrConvertVerifier : RuleVerifier<SargabilityFinding>
{
    public override string RuleId => FindingRuleIds.Tier1CastOrConvertOnColumnRuleId;

    protected override Perturbation Describe(SargabilityFinding finding) =>
        ComputedColumnProbe.Describe(finding.TableQualifiedName, finding.ColumnName, finding.PredicateFragmentText);
}
