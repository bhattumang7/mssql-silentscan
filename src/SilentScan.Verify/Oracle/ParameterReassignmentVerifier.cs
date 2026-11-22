using System.Text.RegularExpressions;
using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed class ParameterReassignmentVerifier : RuleVerifier<ParameterReassignmentPredicateFinding>
{
    public override string RuleId => FindingRuleIds.ParameterReassignmentPredicateRuleId;

    protected override Perturbation Describe(ParameterReassignmentPredicateFinding finding)
    {
        var statement = new Regex(
            @"(\bWHERE\b(?:(?!OPTION)[^;])*" + Regex.Escape(finding.ParameterName) + @"\b(?:(?!OPTION)[^;])*)(;)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return new Perturbation(
            [],
            Difference.EstimatesChanged,
            static (_, _, _) => Task.FromResult(0),
            (connection, transaction, cancellationToken) => ModuleRecompiler.RecompileAsync(
                connection, transaction, finding.SourcePath, string.Empty, definition => statement.Replace(definition, "$1 OPTION (RECOMPILE)$2", 1), cancellationToken),
            static _ => false,
            planCompiledInModule: true);
    }
}
