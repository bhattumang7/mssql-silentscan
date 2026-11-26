using System.Text.RegularExpressions;
using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed class CatchAllPredicateVerifier : RuleVerifier<CatchAllPredicateFinding>
{
    public override string RuleId => FindingRuleIds.CatchAllPredicateRuleId;

    protected override Perturbation Describe(CatchAllPredicateFinding finding)
    {
        var statement = VerifierRegex.Create(
            @"(\bWHERE\b(?:(?!OPTION)[^;])*" + Regex.Escape(finding.ParameterName) + @"\b(?:(?!OPTION)[^;])*)(;)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return new Perturbation(
            [],
            Difference.PlanChanged,
            static (_, _, _) => Task.FromResult(0),
            (connection, transaction, cancellationToken) => ModuleRecompiler.RecompileAsync(
                connection, transaction, finding.SourcePath, string.Empty, definition => statement.Replace(definition, "$1 OPTION (RECOMPILE)$2", 1), cancellationToken),
            static _ => false,
            options: new PerturbationOptions(PlanCompiledInModule: true));
    }
}
