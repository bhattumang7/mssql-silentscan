using System.Text.RegularExpressions;
using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed class TableVariableStaleEstimateInLoopVerifier : RuleVerifier<QueryAntiPatternFinding>
{
    public override string RuleId => FindingRuleIds.QueryAntiPatternTableVariableStaleEstimateInLoopRuleId;

    protected override Perturbation Describe(QueryAntiPatternFinding finding)
    {
        var variable = finding.DetailText.Split(' ', 2)[0];
        var statement = VerifierRegex.Create(
            @"(\bSELECT\b(?:(?!OPTION)[^;])*" + Regex.Escape(variable) + @"\b(?:(?!OPTION)[^;])*)(;)",
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
