using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed class SetOptionAnsiNullsOffVerifier : RuleVerifier<SetOptionFinding>
{
    public override string RuleId => FindingRuleIds.SetOptionAnsiNullsOffBlocksIndexedFeatureRuleId;

    protected override Perturbation Describe(SetOptionFinding finding) =>
        new(
            [],
            Difference.PlanChanged,
            static (_, _, _) => Task.FromResult(0),
            (connection, transaction, cancellationToken) => ModuleRecompiler.RecompileAsync(
                connection, transaction, finding.ModuleQualifiedName, "SET ANSI_NULLS ON;", null, cancellationToken),
            static _ => false,
            options: new PerturbationOptions(PlanCompiledInModule: true));
}
