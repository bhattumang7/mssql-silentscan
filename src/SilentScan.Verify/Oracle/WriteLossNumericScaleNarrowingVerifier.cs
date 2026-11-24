using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed class WriteLossNumericScaleNarrowingVerifier : RuleVerifier<WriteLossFinding>
{
    public override string RuleId => FindingRuleIds.WriteLossNumericScaleNarrowingRuleId;

    protected override Perturbation Describe(WriteLossFinding finding) =>
        WidenedVariableProbe.Describe(
            finding.Target.ToString(),
            finding.ColumnName,
            finding.SourcePath,
            @"(?:decimal|numeric)\s*\(\s*\d+\s*,\s*\d+\s*\)",
            static _ => "DECIMAL(38, 12)");
}
