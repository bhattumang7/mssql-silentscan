using System.Text.RegularExpressions;
using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed class OutputReturnedToClientVerifier : RuleVerifier<ForcedSerialFinding>
{
    public override string RuleId => FindingRuleIds.ForcedSerialOutputReturnedToClientRuleId;

    protected override Perturbation Describe(ForcedSerialFinding finding)
    {
        if (finding.DetailText is not { Length: > 0 } clause)
        {
            return Perturbation.NotApplicable("The finding does not carry the OUTPUT clause text.");
        }

        var withoutInto = VerifierRegex.Create(Regex.Escape(clause) + @"(?!\s+INTO\b)", RegexOptions.IgnoreCase);
        return new Perturbation(
            [],
            Difference.PlanChanged,
            static (_, _, _) => Task.FromResult(0),
            (connection, transaction, cancellationToken) => ModuleRecompiler.RecompileAsync(
                connection,
                transaction,
                finding.SourcePath,
                string.Empty,
                definition => withoutInto.Replace(definition, string.Empty, 1),
                cancellationToken),
            static _ => false,
            options: new PerturbationOptions(PlanCompiledInModule: true));
    }
}
