using SilentScan.Core.Reporting;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Architecture;

public sealed class RuleConditionCoverageTests
{
    private static readonly RuleCondition[] Conditions =
    [
        new("fires-a", RuleConditionKind.Fires, "a"),
        new("excluded-b", RuleConditionKind.Excluded, "b"),
    ];

    private static OracleCase Firing(string name, string? proves = null) => new(name, string.Empty, "SELECT 1;", proves);

    private static SilentCase Silent(string name, string? proves = null) => new(Firing(name, proves), "fires-a");

    [Fact]
    public void EveryCaseAndEveryEntryLineUp_NoProblems()
    {
        var problems = RuleConditionCoverage.Problems(Conditions, [Firing("fires-a")], [Silent("excluded-b")]);

        Assert.Empty(problems);
    }

    [Fact]
    public void CaseNameDifferentFromItsEntry_IsFineWhenItProvesTheEntry()
    {
        var problems = RuleConditionCoverage.Problems(Conditions, [Firing("select-form", "fires-a")], [Silent("update-form", "excluded-b")]);

        Assert.Empty(problems);
    }

    [Fact]
    public void FiringConditionWithNoFiringCase_IsReported()
    {
        var problems = RuleConditionCoverage.Problems(Conditions, [], [Silent("excluded-b")]);

        Assert.Contains(problems, p => p.Contains("firing condition 'fires-a' has no firing case", StringComparison.Ordinal));
    }

    [Fact]
    public void ExclusionWithNoSilentCase_IsReported()
    {
        var problems = RuleConditionCoverage.Problems(Conditions, [Firing("fires-a")], []);

        Assert.Contains(problems, p => p.Contains("exclusion 'excluded-b' has no silent case", StringComparison.Ordinal));
    }

    [Fact]
    public void CaseNamingAnEntryThatDoesNotExist_IsReported()
    {
        var problems = RuleConditionCoverage.Problems(Conditions, [Firing("fires-a"), Firing("invented")], [Silent("excluded-b")]);

        Assert.Contains(problems, p => p.Contains("'invented'", StringComparison.Ordinal));
    }

    [Fact]
    public void FiringCaseNamingAnExclusion_IsReported()
    {
        var problems = RuleConditionCoverage.Problems(Conditions, [Firing("fires-a"), Firing("excluded-b")], [Silent("excluded-b")]);

        Assert.Contains(problems, p => p.Contains("firing case 'excluded-b'", StringComparison.Ordinal));
    }

    [Fact]
    public void RuleWithNoDeclaredConditions_IsReported()
    {
        var problems = RuleConditionCoverage.Problems([], [Firing("fires-a")], [Silent("excluded-b")]);

        Assert.Contains(problems, p => p.Contains("declares no firing conditions", StringComparison.Ordinal));
    }

    [Fact]
    public void EveryPairedOracleCoversExactlyTheConditionsItsRuleDeclares()
    {
        var failures = new List<string>();
        foreach (var type in typeof(RuleConditionCoverageTests).Assembly.GetTypes()
                     .Where(t => t is { IsAbstract: false, IsPublic: true } && typeof(RuleOracle).IsAssignableFrom(t)))
        {
            var oracle = (RuleOracle)Activator.CreateInstance(type)!;
            var rule = RuleCatalog.BaseRules.Single(r => string.Equals(r.Id, oracle.DeclaredRuleId, StringComparison.Ordinal));
            failures.AddRange(RuleConditionCoverage
                .Problems(rule.DeclaredConditions, oracle.DeclaredFiringCases, oracle.DeclaredSilentCases)
                .Select(p => $"{type.Name}: {p}"));
        }

        Assert.True(failures.Count == 0, string.Join('\n', failures));
    }
}
