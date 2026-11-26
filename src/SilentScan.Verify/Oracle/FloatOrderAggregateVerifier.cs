using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed class FloatOrderAggregateVerifier : RuleVerifier<FloatOrderDependentAggregateFinding>
{
    public override string RuleId => FindingRuleIds.FloatOrderDependentAggregateRuleId;

    protected override Perturbation Describe(FloatOrderDependentAggregateFinding finding) =>
        ScratchIndexOrderProbe.Describe(finding.TableQualifiedName, finding.ColumnName);
}
