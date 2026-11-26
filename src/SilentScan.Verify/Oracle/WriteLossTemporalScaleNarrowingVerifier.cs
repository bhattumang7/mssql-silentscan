using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed class WriteLossTemporalScaleNarrowingVerifier : RuleVerifier<WriteLossFinding>
{
    public override string RuleId => FindingRuleIds.WriteLossTemporalScaleNarrowingRuleId;

    protected override Perturbation Describe(WriteLossFinding finding) =>
        WidenedVariableProbe.Describe(
            finding.Target.ToString(),
            finding.ColumnName,
            finding.SourcePath,
            @"(?:datetime2|time|datetimeoffset)\s*\(\s*\d+\s*\)|datetime\b(?!\s*\()",
            static type => type.Equals("datetime", StringComparison.OrdinalIgnoreCase) ? "DATETIME2(7)" : type.Split('(')[0].Trim() + "(7)");
}
