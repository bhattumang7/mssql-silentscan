using System.Text.RegularExpressions;
using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed partial class ExactApproximateComparisonVerifier : RuleVerifier<ExactApproximateComparisonFinding>
{
    public override string RuleId => FindingRuleIds.ExactApproximateComparisonRuleId;

    [GeneratedRegex(@"^[A-Za-z0-9_(),]+$")]
    private static partial Regex PlainTypeName();

    protected override Perturbation Describe(ExactApproximateComparisonFinding finding)
    {
        var split = finding.ApproximateColumn.LastIndexOf('.');
        if (split <= 0
            || ColumnAddress.TryParse(finding.ApproximateColumn[..split], finding.ApproximateColumn[(split + 1)..]) is not { } column
            || !PlainTypeName().IsMatch(finding.ExactTypeDisplay))
        {
            return Perturbation.NotApplicable("The finding does not name a plain column and exact type the probe can address.");
        }

        return new Perturbation(
            [column.Touched],
            Difference.ResultChanged,
            static (_, _, _) => Task.FromResult(0),
            (connection, transaction, cancellationToken) => ColumnAddress.ExecuteAsync(
                connection, transaction, $"ALTER TABLE {column.Table} ALTER COLUMN {column.Column} {finding.ExactTypeDisplay} NOT NULL;", cancellationToken),
            static _ => false);
    }
}
