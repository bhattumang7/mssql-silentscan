using System.Reflection;
using SilentScan.Core.Reporting;

namespace SilentScan.Tests.Architecture;

public sealed class RuleOracleCoverageTests
{
    private const string CategoryTrait = "Category";
    private const string OracleCategory = "Oracle";
    private const string RuleTrait = "Rule";

    private static readonly string[] StyleRulePrefixes =
    [
        "silentscan/formatting/",
        "silentscan/metrics/",
        "silentscan/dead-code/",
        "silentscan/duplication/",
        "silentscan/deprecated-syntax/task-comment-",
        "silentscan/control-flow/goto-usage",
    ];

    private static IEnumerable<string> RuleIdsClaimedByOracleTests()
    {
        foreach (var type in typeof(RuleOracleCoverageTests).Assembly.GetTypes())
        {
            var typeTraits = TraitsOf(type);
            var typeIsOracle = typeTraits.Contains((CategoryTrait, OracleCategory));

            foreach (var (_, value) in typeTraits.Where(t => t.Name == RuleTrait && typeIsOracle))
            {
                yield return value;
            }

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                var methodTraits = TraitsOf(method);
                var methodIsOracle = typeIsOracle || methodTraits.Contains((CategoryTrait, OracleCategory));

                foreach (var (_, value) in methodTraits.Where(t => t.Name == RuleTrait && methodIsOracle))
                {
                    yield return value;
                }
            }
        }
    }

    private static List<(string Name, string Value)> TraitsOf(MemberInfo member) =>
        [.. member.GetCustomAttributesData()
            .Where(data => data.AttributeType == typeof(TraitAttribute))
            .Select(data => ((string)data.ConstructorArguments[0].Value!, (string)data.ConstructorArguments[1].Value!))];

    [Fact]
    public void EveryPublishedRuleIsBackedByAnOracleTest()
    {
        var claimed = RuleIdsClaimedByOracleTests().ToHashSet(StringComparer.Ordinal);

        var unbacked = RuleCatalog.BaseRules
            .Select(rule => rule.Id)
            .Where(id => !claimed.Contains(id))
            .Where(id => !StyleRulePrefixes.Any(prefix => id.StartsWith(prefix, StringComparison.Ordinal)))
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            unbacked.Count == 0,
            $"{unbacked.Count} published rule(s) have no [Trait(\"Category\", \"Oracle\")] test tagged [Trait(\"Rule\", \"<id>\")]:\n{string.Join('\n', unbacked)}");
    }

    [Fact]
    public void EveryRuleTraitNamesAPublishedRule()
    {
        var published = RuleCatalog.BaseRules.Select(rule => rule.Id).ToHashSet(StringComparer.Ordinal);

        var unknown = RuleIdsClaimedByOracleTests()
            .Where(id => !published.Contains(id))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            unknown.Count == 0,
            $"{unknown.Count} [Trait(\"Rule\", ...)] value(s) name no published rule:\n{string.Join('\n', unknown)}");
    }
}
