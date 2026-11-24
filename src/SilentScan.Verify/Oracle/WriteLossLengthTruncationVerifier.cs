using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed class WriteLossLengthTruncationVerifier : RuleVerifier<WriteLossFinding>
{
    public override string RuleId => FindingRuleIds.WriteLossLengthTruncationRuleId;

    protected override Perturbation Describe(WriteLossFinding finding) =>
        WidenedVariableProbe.Describe(
            finding.Target.ToString(),
            finding.ColumnName,
            finding.SourcePath,
            @"(?:n?var(?:char|binary)|n?char|binary)\s*\(\s*\d+\s*\)",
            static type => type.ToUpperInvariant() switch
            {
                var t when t.StartsWith("NVARCHAR", StringComparison.Ordinal) || t.StartsWith("NCHAR", StringComparison.Ordinal) => "NVARCHAR(MAX)",
                var t when t.StartsWith("VARBINARY", StringComparison.Ordinal) || t.StartsWith("BINARY", StringComparison.Ordinal) => "VARBINARY(MAX)",
                _ => "VARCHAR(MAX)",
            });
}
