using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed class JsonArrayAggOrderVerifier : RuleVerifier<JsonArrayAggMissingOrderFinding>
{
    public override string RuleId => FindingRuleIds.JsonArrayAggMissingOrderRuleId;

    protected override Perturbation Describe(JsonArrayAggMissingOrderFinding finding) =>
        ScratchIndexOrderProbe.Describe(finding.TableQualifiedName, finding.ColumnName);
}
