using SilentScan.Tests.Support;
using SilentScan.Verify.Commands;
using SilentScan.Verify.Oracle;

namespace SilentScan.Tests.Commands;

public sealed class GenerateScopeCorpusCommandTests
{
    [Fact]
    public void Create_HasNameAndOutputOptionWithCheckedInDefault()
    {
        var command = GenerateScopeCorpusCommand.Create();

        Assert.Equal("generate-scope-corpus", command.Name);
        var outputOption = Assert.Single(command.Options, o => o.Name == "--output");
        Assert.Equal("tests/SilentScan.Tests/fixtures/scope/ScopeScenarioCorpus.json", outputOption.GetDefaultValue());
    }

    [Fact]
    public void Scenarios_HaveUniqueIdsAndCoverTheWholeSpec()
    {
        var ids = ScopeScenarios.All.Select(s => s.Id).ToList();

        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.True(ScopeScenarios.All.Count >= 30);
        Assert.True(ScopeScenarios.All.Select(s => s.Family).Distinct().Count() >= 8);
        Assert.Contains(ScopeScenarios.All, s => s.Kind == ScopeObservableKind.Binding);
        Assert.Contains(ScopeScenarios.All, s => s.CaseSensitiveDatabase);
    }

    [Fact]
    public void CheckedInCorpus_RecordsEveryScenarioWithAnObservation()
    {
        var corpus = ScopeCorpus.Load();

        Assert.Equal(ScopeScenarios.All.Select(s => s.Id), corpus.Select(e => e.Id));
        Assert.All(corpus, e => Assert.False(string.IsNullOrEmpty(e.Observed), e.Id));
    }
}
