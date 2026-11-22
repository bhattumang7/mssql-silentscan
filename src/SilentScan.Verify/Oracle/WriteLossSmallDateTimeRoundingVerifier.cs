using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed class WriteLossSmallDateTimeRoundingVerifier : RuleVerifier<WriteLossFinding>
{
    public override string RuleId => FindingRuleIds.WriteLossSmallDateTimeRoundingRuleId;

    protected override Perturbation Describe(WriteLossFinding finding) =>
        WidenedVariableProbe.Describe(
            finding.TableQualifiedName,
            finding.ColumnName,
            finding.SourcePath,
            @"smalldatetime",
            static _ => "DATETIME2(7)");
}
