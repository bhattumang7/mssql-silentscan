using System.Text.RegularExpressions;
using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed class IsNullReplacementTruncationVerifier : RuleVerifier<IsNullReplacementValueTruncationFinding>
{
    public override string RuleId => FindingRuleIds.IsNullReplacementValueTruncationRuleId;

    protected override Perturbation Describe(IsNullReplacementValueTruncationFinding finding)
    {
        var call = new Regex(
            @"\bISNULL\s*\(\s*" + Regex.Escape(finding.CheckExpressionDisplay) + @"\s*,\s*" + Regex.Escape(finding.ReplacementValueDisplay) + @"\s*\)",
            RegexOptions.IgnoreCase);
        return new Perturbation(
            [],
            Difference.ResultChanged,
            static (_, _, _) => Task.FromResult(0),
            static (_, _, _) => Task.FromResult(0),
            static _ => false,
            baselineStatement: statement => call.Count(statement) == 1
                ? call.Replace(statement, $"COALESCE({finding.CheckExpressionDisplay}, {finding.ReplacementValueDisplay})")
                : null);
    }
}
