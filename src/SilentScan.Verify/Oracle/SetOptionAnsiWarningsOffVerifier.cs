using System.Text.RegularExpressions;
using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed partial class SetOptionAnsiWarningsOffVerifier : RuleVerifier<SetOptionFinding>
{
    public override string RuleId => FindingRuleIds.SetOptionAnsiWarningsOffBlocksIndexedFeatureRuleId;

    [GeneratedRegex(@"SET\s+ANSI_WARNINGS\s+OFF", RegexOptions.IgnoreCase)]
    private static partial Regex Setting();

    protected override Perturbation Describe(SetOptionFinding finding) =>
        new(
            [],
            Difference.PlanChanged,
            static (_, _, _) => Task.FromResult(0),
            (connection, transaction, cancellationToken) => ModuleRecompiler.RecompileAsync(
                connection, transaction, finding.ModuleQualifiedName, string.Empty, definition => Setting().Replace(definition, "SET ANSI_WARNINGS ON"), cancellationToken),
            static _ => false,
            options: new PerturbationOptions(PlanCompiledInModule: true));
}
