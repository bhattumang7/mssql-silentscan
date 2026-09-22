using SilentScan.Core.Reporting.RuleHarness;

namespace SilentScan.Tests.Architecture;

public sealed class RuleDynamicSqlApplicabilityTests
{
    [Fact]
    public void EveryRegisteredRuleDeclaresADynamicSqlApplicability()
    {
        var undeclared = RuleRegistry.All
            .Where(rule => !Enum.IsDefined(rule.DynamicSql))
            .Select(rule => rule.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            undeclared.Count == 0,
            $"{undeclared.Count} registered rule(s) declare no valid DynamicSqlApplicability:\n{string.Join('\n', undeclared)}");
    }
}
