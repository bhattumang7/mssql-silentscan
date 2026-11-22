using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed class WriteLossTemporalPrecisionLossVerifier : RuleVerifier<WriteLossFinding>
{
    public override string RuleId => FindingRuleIds.WriteLossTemporalPrecisionLossRuleId;

    protected override Perturbation Describe(WriteLossFinding finding) =>
        WidenedVariableProbe.Describe(
            finding.TableQualifiedName,
            finding.ColumnName,
            finding.SourcePath,
            @"date(?![\w(])",
            static _ => "DATETIME2(7)");
}
