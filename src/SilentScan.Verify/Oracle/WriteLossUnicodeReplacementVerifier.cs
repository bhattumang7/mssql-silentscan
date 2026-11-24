using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed class WriteLossUnicodeReplacementVerifier : RuleVerifier<WriteLossFinding>
{
    public override string RuleId => FindingRuleIds.WriteLossUnicodeReplacementRuleId;

    protected override Perturbation Describe(WriteLossFinding finding) =>
        WidenedVariableProbe.Describe(
            finding.Target.ToString(),
            finding.ColumnName,
            finding.SourcePath,
            @"(?:var)?char\s*\(\s*\d+\s*\)",
            static type => "N" + type);
}
