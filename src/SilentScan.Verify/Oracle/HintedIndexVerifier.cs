using System.Text.RegularExpressions;
using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed class HintedIndexVerifier : RuleVerifier<IndexHintFinding>
{
    public override string RuleId => FindingRuleIds.IndexHintHintedIndexNotSeekableRuleId;

    protected override Perturbation Describe(IndexHintFinding finding)
    {
        var hint = VerifierRegex.Create(
            @"\bWITH\s*\(\s*INDEX\s*(?:\(\s*|=\s*)\[?" + Regex.Escape(finding.HintedIndexName) + @"\]?\s*\)?\s*\)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return new Perturbation(
            [],
            Difference.PlanChanged,
            static (_, _, _) => Task.FromResult(0),
            (connection, transaction, cancellationToken) => ModuleRecompiler.RecompileAsync(
                connection, transaction, finding.SourcePath, string.Empty, definition => hint.Replace(definition, string.Empty, 1), cancellationToken),
            static _ => false,
            options: new PerturbationOptions(PlanCompiledInModule: true));
    }
}
