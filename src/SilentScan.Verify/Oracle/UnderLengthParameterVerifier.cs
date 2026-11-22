using System.Globalization;
using System.Text.RegularExpressions;
using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed class UnderLengthParameterVerifier : RuleVerifier<UnderLengthParameterFinding>
{
    public override string RuleId => FindingRuleIds.UnderLengthParameterRuleId;

    protected override Perturbation Describe(UnderLengthParameterFinding finding)
    {
        var size = finding.IsImplicitDefault
            ? @"(?!\s*\()"
            : finding.OtherOperandLength is { } length
                ? @"\s*\(\s*" + length.ToString(CultureInfo.InvariantCulture) + @"\s*\)"
                : null;
        if (size is null)
        {
            return Perturbation.NotApplicable("The finding does not say how long the other operand is.");
        }

        var declaration = new Regex(@"(@\w+\s+(?:AS\s+)?)(n?varchar|n?char)" + size, RegexOptions.IgnoreCase);
        string? original = null;
        return new Perturbation(
            [],
            Difference.ResultChanged,
            (connection, transaction, cancellationToken) => ModuleRecompiler.RecompileAsync(
                connection,
                transaction,
                finding.SourcePath,
                string.Empty,
                definition =>
                {
                    original = definition;
                    return declaration.Replace(definition, "${1}${2}(MAX)");
                },
                cancellationToken),
            (connection, transaction, cancellationToken) => ModuleRecompiler.RecompileAsync(
                connection, transaction, finding.SourcePath, string.Empty, definition => original ?? definition, cancellationToken),
            static _ => false);
    }
}
