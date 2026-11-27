using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;

namespace SilentScan.Verify.Oracle;

public sealed class Tier1RegexpSeekDropsMatchesVerifier : RuleVerifier<SargabilityFinding>
{
    private const string Filter = " WHERE ";

    public override string RuleId => FindingRuleIds.Tier1RegexpSeekDropsMatchesRuleId;

    protected override Perturbation Describe(SargabilityFinding finding)
    {
        if (finding.PredicateFragmentText is null || finding.TableQualifiedName?.Split('.', 2) is not [var schema, var table])
        {
            return Perturbation.NotApplicable("The finding does not name the table and predicate the probe has to read.");
        }

        return new Perturbation(
            [new TouchedObject(schema, table)],
            Difference.ResultChanged,
            static (_, _, _) => Task.FromResult(0),
            static (_, _, _) => Task.FromResult(0),
            static _ => false,
            baselineStatement: statement => Hinted(statement, "INDEX(0)"),
            options: new PerturbationOptions(AppliedStatement: statement => Hinted(statement, "FORCESEEK")));
    }

    private static string? Hinted(string statement, string hint)
    {
        var filter = statement.IndexOf(Filter, StringComparison.OrdinalIgnoreCase);
        return filter < 0 ? null : statement.Insert(filter, $" WITH ({hint})");
    }
}
