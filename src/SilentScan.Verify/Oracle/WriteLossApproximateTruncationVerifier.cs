using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed class WriteLossApproximateTruncationVerifier : RuleVerifier<WriteLossFinding>
{
    public override string RuleId => FindingRuleIds.WriteLossApproximateTruncationRuleId;

    protected override Perturbation Describe(WriteLossFinding finding) =>
        WidenedVariableProbe.Describe(
            finding.TableQualifiedName,
            finding.ColumnName,
            finding.SourcePath,
            @"int(?![\w])",
            static _ => "FLOAT");
}
