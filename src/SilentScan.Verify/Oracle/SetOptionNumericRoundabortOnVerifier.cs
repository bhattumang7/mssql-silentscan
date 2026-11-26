using System.Text.RegularExpressions;
using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed partial class SetOptionNumericRoundabortOnVerifier : RuleVerifier<SetOptionFinding>
{
    public override string RuleId => FindingRuleIds.SetOptionNumericRoundabortOnBlocksIndexedFeatureRuleId;

    [GeneratedRegex(@"SET\s+NUMERIC_ROUNDABORT\s+ON", RegexOptions.IgnoreCase)]
    private static partial Regex Setting();

    protected override Perturbation Describe(SetOptionFinding finding) =>
        new(
            [],
            Difference.PlanChanged,
            static (_, _, _) => Task.FromResult(0),
            (connection, transaction, cancellationToken) => ModuleRecompiler.RecompileAsync(
                connection, transaction, finding.ModuleQualifiedName, string.Empty, definition => Setting().Replace(definition, "SET NUMERIC_ROUNDABORT OFF"), cancellationToken),
            static _ => false,
            options: new PerturbationOptions(PlanCompiledInModule: true));
}
