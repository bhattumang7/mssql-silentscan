using SilentScan.Core.Reporting.RuleDocs;
using SilentScan.Core.Rules;
using SilentScan.Live.Sweep;
using SilentScan.Verify;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class BehaviorSweepRunnerTests
{
    [Fact]
    public async Task RunAsync_DocumentedScalarExamples_RequireBothTheClaimedValueAndTheMatchingFinding()
    {
        var cases = RuleExampleCorpus.Build()
            .Where(@case => @case.RuleId == FindingRuleIds.StringConcatNullRuleId)
            .ToList();
        var results = await BehaviorSweepRunner.RunAsync(cases, SqlServerOptions.LocalDocker);

        Assert.Equal(2, results.Count);
        Assert.All(results, result => Assert.Equal(SweepOutcome.Confirmed, result.Outcome));
    }
}
