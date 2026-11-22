using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed class ExecResultSetsShapeVerifier : RuleVerifier<ExecResultSetsShapeFinding>
{
    public override string RuleId => FindingRuleIds.ExecResultSetsShapeColumnTypeMismatchRuleId;

    protected override Perturbation Describe(ExecResultSetsShapeFinding finding) =>
        DeclaredColumnTypeProbe.Describe(
            finding.CallerScopeQualifiedName, finding.ColumnName, finding.DeclaredColumnTypeDisplay, finding.DescribedColumnTypeDisplay);
}
