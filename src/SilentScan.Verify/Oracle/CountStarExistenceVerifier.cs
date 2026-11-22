using System.Text.RegularExpressions;
using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed class CountStarExistenceVerifier : RuleVerifier<QueryAntiPatternFinding>
{
    public override string RuleId => FindingRuleIds.QueryAntiPatternCountStarVariableExistenceCheckRuleId;

    private static string Inline(Match match) =>
        match.Groups[2].Success
            ? $"IF (SELECT COUNT(*) {match.Groups[1].Value}) {match.Groups[2].Value} {match.Groups[3].Value}"
            : $"IF {match.Groups[4].Value} {match.Groups[5].Value} (SELECT COUNT(*) {match.Groups[1].Value})";

    protected override Perturbation Describe(QueryAntiPatternFinding finding)
    {
        var variable = finding.DetailText.Split(' ', 2)[0];
        var name = Regex.Escape(variable);
        var assignment = new Regex(
            @"SELECT\s+" + name + @"\s*=\s*COUNT\s*\(\s*\*\s*\)\s+(FROM\s+[^;]+?)\s*;\s*IF\s+(?:" + name + @"\s*(>=|>|=|<>|!=)\s*([01])\b|([01])\s*(<=|<|=|<>|!=)\s*" + name + @"(?![\w@]))",
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
                definition => assignment.Replace(definition, Inline, 1),
                cancellationToken),
            static _ => false,
            planCompiledInModule: true);
    }
}
