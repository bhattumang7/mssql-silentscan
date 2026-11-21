using System.Reflection;
using SilentScan.Core.Reporting;
using SilentScan.Tests.Support;
using SilentScan.Verify.Oracle;

namespace SilentScan.Tests.Architecture;

public sealed class RuleVerifierCoverageTests
{
    private static IEnumerable<(Type Type, string RuleId)> PairedOracles() =>
        typeof(RuleVerifierCoverageTests).Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsClass: true } && typeof(RuleOracle).IsAssignableFrom(t))
            .SelectMany(t => t.GetCustomAttributesData()
                .Where(d => d.AttributeType == typeof(TraitAttribute) && (string)d.ConstructorArguments[0].Value! == "Rule")
                .Select(d => (t, (string)d.ConstructorArguments[1].Value!)));

    [Fact]
    public void EveryRuleOracleHasARegisteredVerifierForItsRule()
    {
        var missing = PairedOracles()
            .Where(o => RuleVerifierRegistry.For(o.RuleId) is null)
            .Select(o => $"{o.Type.Name}: {o.RuleId}")
            .ToList();

        Assert.True(missing.Count == 0, $"RuleOracle tests whose rule has no RuleVerifier:\n{string.Join('\n', missing)}");
    }

    [Fact]
    public void EveryRegisteredVerifierNamesAPublishedRule()
    {
        var published = RuleCatalog.BaseRules.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);

        var unknown = RuleVerifierRegistry.All.Keys.Where(id => !published.Contains(id)).Order(StringComparer.Ordinal).ToList();

        Assert.True(unknown.Count == 0, $"Registered verifiers naming no published rule:\n{string.Join('\n', unknown)}");
    }

    [Fact]
    public void EveryRegisteredVerifierHasARuleOracleTest()
    {
        var paired = PairedOracles().Select(o => o.RuleId).ToHashSet(StringComparer.Ordinal);

        var orphaned = RuleVerifierRegistry.All.Keys.Where(id => !paired.Contains(id)).Order(StringComparer.Ordinal).ToList();

        Assert.True(orphaned.Count == 0, $"Verifiers with no RuleOracle test exercising them:\n{string.Join('\n', orphaned)}");
    }
}
