using System.Text.RegularExpressions;
using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed class CountStarExistenceVerifier : RuleVerifier<QueryAntiPatternFinding>
{
    public override string RuleId => FindingRuleIds.QueryAntiPatternCountStarVariableExistenceCheckRuleId;

    protected override Perturbation Describe(QueryAntiPatternFinding finding)
    {
        var variable = finding.DetailText.Split(' ', 2)[0];
        var assignment = new Regex(
            @"SELECT\s+" + Regex.Escape(variable) + @"\s*=\s*COUNT\s*\(\s*\*\s*\)\s+(FROM\s+[^;]+?)\s*;\s*IF\s+" + Regex.Escape(variable) + @"\s*>\s*0\b",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return new Perturbation(
            [],
            Difference.PlanChanged,
            static (_, _, _) => Task.FromResult(0),
            (connection, transaction, cancellationToken) => ModuleRecompiler.RecompileAsync(
                connection,
                transaction,
                finding.SourcePath,
                string.Empty,
                definition => assignment.Replace(definition, "IF (SELECT COUNT(*) $1) > 0", 1),
                cancellationToken),
            static _ => false,
            planCompiledInModule: true);
    }
}
