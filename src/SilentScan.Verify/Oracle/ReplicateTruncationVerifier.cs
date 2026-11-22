using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed class ReplicateTruncationVerifier : RuleVerifier<BoundedStringBuiltinTruncationFinding>
{
    public override string RuleId => FindingRuleIds.BoundedStringBuiltinTruncationReplicateResultTruncatedRuleId;

    protected override Perturbation Describe(BoundedStringBuiltinTruncationFinding finding) =>
        new(
            [],
            Difference.ResultChanged,
            static (_, _, _) => Task.FromResult(0),
            static (_, _, _) => Task.FromResult(0),
            static _ => false,
            baselineStatement: statement => StringBuiltinWidening.Rewrite(statement, finding.FunctionName));
}
