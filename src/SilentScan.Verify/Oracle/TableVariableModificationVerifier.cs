using System.Text.RegularExpressions;
using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed class TableVariableModificationVerifier : RuleVerifier<ForcedSerialFinding>
{
    public override string RuleId => FindingRuleIds.ForcedSerialTableVariableModificationRuleId;

    protected override Perturbation Describe(ForcedSerialFinding finding)
    {
        if (finding.DetailText is not { Length: > 1 } variable || variable[0] != '@')
        {
            return Perturbation.NotApplicable("The finding does not name the table variable.");
        }

        var declaration = VerifierRegex.Create(@"\bDECLARE\s+" + Regex.Escape(variable) + @"\s+TABLE\s*\(", RegexOptions.IgnoreCase);
        var reference = VerifierRegex.Create(Regex.Escape(variable) + @"\b", RegexOptions.IgnoreCase);
        var replacement = "#" + variable[1..];
        return new Perturbation(
            [],
            Difference.PlanChanged,
            static (_, _, _) => Task.FromResult(0),
            (connection, transaction, cancellationToken) => ModuleRecompiler.RecompileAsync(
                connection,
                transaction,
                finding.SourcePath,
                string.Empty,
                definition => reference.Replace(declaration.Replace(definition, "CREATE TABLE " + replacement + " (", 1), replacement),
                cancellationToken),
            static _ => false,
            planCompiledInModule: true);
    }
}
