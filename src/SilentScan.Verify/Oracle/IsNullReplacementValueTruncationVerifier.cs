using System.Text.RegularExpressions;
using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed partial class IsNullReplacementValueTruncationVerifier : RuleVerifier<IsNullReplacementValueTruncationFinding>
{
    public override string RuleId => FindingRuleIds.IsNullReplacementValueTruncationRuleId;

    [GeneratedRegex(@"\bISNULL\s*\(", RegexOptions.IgnoreCase)]
    private static partial Regex IsNullCall();

    protected override Perturbation Describe(IsNullReplacementValueTruncationFinding finding) =>
        new(
            [],
            Difference.ResultChanged,
            static (_, _, _) => Task.CompletedTask,
            static (_, _, _) => Task.CompletedTask,
            static _ => false,
            static statement => IsNullCall().Count(statement) == 1 ? IsNullCall().Replace(statement, "COALESCE(") : null);
}
