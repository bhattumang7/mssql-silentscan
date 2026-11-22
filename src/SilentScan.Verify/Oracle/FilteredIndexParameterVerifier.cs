using System.Text.RegularExpressions;
using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed class FilteredIndexParameterVerifier : RuleVerifier<FilteredIndexParameterMismatchFinding>
{
    public override string RuleId => FindingRuleIds.FilteredIndexParameterMismatchRuleId;

    protected override Perturbation Describe(FilteredIndexParameterMismatchFinding finding)
    {
        var statement = new Regex(
            @"(\bWHERE\b(?:(?!OPTION)[^;])*" + Regex.Escape(finding.VariableName) + @"\b(?:(?!OPTION)[^;])*)(;)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return new Perturbation(
            [],
            Difference.PlanChanged,
            static (_, _, _) => Task.FromResult(0),
            (connection, transaction, cancellationToken) => ModuleRecompiler.RecompileAsync(
                connection, transaction, finding.SourcePath, string.Empty, definition => statement.Replace(definition, "$1 OPTION (RECOMPILE)$2", 1), cancellationToken),
            static _ => false,
            planCompiledInModule: true);
    }
}
