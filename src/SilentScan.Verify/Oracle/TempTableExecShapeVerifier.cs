using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed class TempTableExecShapeVerifier : RuleVerifier<TempTableExecShapeFinding>
{
    public override string RuleId => FindingRuleIds.TempTableExecShapeColumnTypeMismatchRuleId;

    protected override Perturbation Describe(TempTableExecShapeFinding finding) =>
        DeclaredColumnTypeProbe.Describe(
            finding.CallerScopeQualifiedName, finding.ColumnName, finding.TempColumnTypeDisplay, finding.DescribedColumnTypeDisplay);
}
