using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed class WriteLossTemporalOffsetDroppedVerifier : RuleVerifier<WriteLossFinding>
{
    public override string RuleId => FindingRuleIds.WriteLossTemporalOffsetDroppedRuleId;

    protected override Perturbation Describe(WriteLossFinding finding) =>
        WidenedVariableProbe.Describe(
            finding.TableQualifiedName,
            finding.ColumnName,
            finding.SourcePath,
            @"datetime2(?:\s*\(\s*\d+\s*\))?|smalldatetime|datetime(?![\w])",
            static _ => "DATETIMEOFFSET(7)");
}
