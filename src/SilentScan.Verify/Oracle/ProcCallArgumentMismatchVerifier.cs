using System.Text.RegularExpressions;
using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed class ProcCallArgumentMismatchVerifier : RuleVerifier<ProcCallArgumentMismatchFinding>
{
    public override string RuleId => FindingRuleIds.ProcCallArgumentMismatchRuleId;

    protected override Perturbation Describe(ProcCallArgumentMismatchFinding finding)
    {
        if (finding.IsOutputWriteback
            || !VerifierRegex.IsMatch(finding.FormalParameterTypeDisplay, @"^(?:n?var)?char\s*\(|^n?char\s*\(|^(?:var)?binary\s*\(", RegexOptions.IgnoreCase))
        {
            return Perturbation.NotApplicable("The probe handles input string and binary parameters only.");
        }

        var declaration = VerifierRegex.Create(
            @"(" + Regex.Escape(finding.FormalParameterName) + @"\s+(?:AS\s+)?)(n?varchar|n?char|varbinary|binary)\s*\(\s*\d+\s*\)",
            RegexOptions.IgnoreCase);
        string? original = null;
        return new Perturbation(
            [],
            Difference.ResultChanged,
            (connection, transaction, cancellationToken) => ModuleRecompiler.RecompileAsync(
                connection,
                transaction,
                finding.CalleeQualifiedName,
                string.Empty,
                definition =>
                {
                    original = definition;
                    return declaration.Replace(definition, match => match.Groups[1].Value + match.Groups[2].Value + "(MAX)", 1);
                },
                cancellationToken),
            (connection, transaction, cancellationToken) => ModuleRecompiler.RecompileAsync(
                connection, transaction, finding.CalleeQualifiedName, string.Empty, definition => original ?? definition, cancellationToken),
            static _ => false);
    }
}
