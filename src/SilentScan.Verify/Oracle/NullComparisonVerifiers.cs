using System.Text.RegularExpressions;
using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

[VerifierSupport]
public abstract class NullComparisonVerifier : RuleVerifier<DeprecatedSyntaxFinding>
{
    protected abstract Regex Comparison { get; }

    protected abstract string Replacement { get; }

    protected override Perturbation Describe(DeprecatedSyntaxFinding finding) =>
        new(
            [],
            Difference.ResultChanged,
            static (_, _, _) => Task.CompletedTask,
            static (_, _, _) => Task.CompletedTask,
            static _ => false,
            statement => Comparison.Count(statement) switch
            {
                0 => statement,
                1 => Comparison.Replace(statement, Replacement),
                _ => null,
            });
}

public sealed partial class EqualsNullVerifier : NullComparisonVerifier
{
    public override string RuleId => FindingRuleIds.DeprecatedSyntaxEqualsNullComparisonRuleId;

    protected override Regex Comparison => EqualsNull();

    protected override string Replacement => "IS NULL";

    [GeneratedRegex(@"(?<![<>!=])=\s*NULL\b", RegexOptions.IgnoreCase)]
    private static partial Regex EqualsNull();
}

public sealed partial class NotEqualsNullVerifier : NullComparisonVerifier
{
    public override string RuleId => FindingRuleIds.DeprecatedSyntaxNotEqualsNullComparisonRuleId;

    protected override Regex Comparison => NotEqualsNull();

    protected override string Replacement => "IS NOT NULL";

    [GeneratedRegex(@"(?:<>|!=)\s*NULL\b", RegexOptions.IgnoreCase)]
    private static partial Regex NotEqualsNull();
}
