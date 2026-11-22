using System.Text.RegularExpressions;
using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed class SpExecuteSqlParameterMismatchVerifier : RuleVerifier<SpExecuteSqlParameterMismatchFinding>
{
    public override string RuleId => FindingRuleIds.SpExecuteSqlParameterMismatchRuleId;

    protected override Perturbation Describe(SpExecuteSqlParameterMismatchFinding finding)
    {
        if (finding.CallerScopeQualifiedName is not { } module
            || finding.IsOutputWriteback
            || !Regex.IsMatch(finding.DeclaredParameterTypeDisplay, @"^(?:n?var)?char\s*\(|^n?char\s*\(|^(?:var)?binary\s*\(", RegexOptions.IgnoreCase))
        {
            return Perturbation.NotApplicable("The probe handles input string and binary parameters declared in a named module only.");
        }

        var declaration = new Regex(
            @"(" + Regex.Escape(finding.ParameterName) + @"\s+(?:AS\s+)?)(n?varchar|n?char|varbinary|binary)\s*\(\s*\d+\s*\)",
            RegexOptions.IgnoreCase);
        string? original = null;
        return new Perturbation(
            [],
            Difference.ResultChanged,
            (connection, transaction, cancellationToken) => ModuleRecompiler.RecompileAsync(
                connection,
                transaction,
                module,
                string.Empty,
                definition =>
                {
                    original = definition;
                    return declaration.Replace(definition, match => match.Groups[1].Value + match.Groups[2].Value + "(MAX)", 1);
                },
                cancellationToken),
            (connection, transaction, cancellationToken) => ModuleRecompiler.RecompileAsync(
                connection, transaction, module, string.Empty, definition => original ?? definition, cancellationToken),
            static _ => false);
    }
}
