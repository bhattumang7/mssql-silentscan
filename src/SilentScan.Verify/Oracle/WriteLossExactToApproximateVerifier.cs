using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed class WriteLossExactToApproximateVerifier : RuleVerifier<WriteLossFinding>
{
    private const string Exact = "DECIMAL(38,4)";

    public override string RuleId => FindingRuleIds.WriteLossExactToApproximateRuleId;

    protected override Perturbation Describe(WriteLossFinding finding) =>
        finding.Target.ToString() == "Column"
            ? WidenedColumnProbe.Describe(finding.TableQualifiedName, finding.ColumnName, finding.SourcePath, static _ => Exact)
            : WidenedVariableProbe.Describe(
                finding.Target.ToString(),
                finding.ColumnName,
                finding.SourcePath,
                @"(?:real|float(?:\s*\(\s*\d+\s*\))?)(?![\w])",
                static _ => Exact);
}
