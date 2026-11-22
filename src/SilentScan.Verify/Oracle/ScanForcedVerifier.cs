using System.Text.RegularExpressions;
using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed class ScanForcedVerifier : RuleVerifier<TypedPredicateFinding>
{
    public override string RuleId => FindingRuleIds.VerdictScanForcedRuleId;

    protected override Perturbation Describe(TypedPredicateFinding finding)
    {
        if (finding.ColumnTypeText is not { Length: > 0 } columnType || finding.OtherOperandText is not { Length: > 0 } operand)
        {
            return Perturbation.NotApplicable("The finding does not carry the column type and the other operand's text.");
        }

        var bareType = Regex.Replace(columnType, @"\s+COLLATE\s+\S+|\s+(?:NOT\s+)?NULL\s*$", string.Empty, RegexOptions.IgnoreCase);
        var occurrence = new Regex(Regex.Escape(operand));
        return new Perturbation(
            [],
            Difference.PlanChanged,
            static (_, _, _) => Task.FromResult(0),
            static (_, _, _) => Task.FromResult(0),
            static _ => false,
            baselineStatement: statement => occurrence.Count(statement) == 1
                ? occurrence.Replace(statement, $"CAST({operand} AS {bareType})")
                : null);
    }
}
