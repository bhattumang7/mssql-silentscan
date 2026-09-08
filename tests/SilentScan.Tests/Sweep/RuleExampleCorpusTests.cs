using SilentScan.Core.Reporting.RuleDocs;
using SilentScan.Live.Sweep;

namespace SilentScan.Tests.Sweep;

public sealed class RuleExampleCorpusTests
{
    [Fact]
    public void Build_ProducesANoncompliantCaseForEveryPublishedExample()
    {
        var cases = RuleExampleCorpus.Build();
        var expectedNoncompliantCount = RuleDocCatalog.ByRuleId.Values.Sum(c => c.AllExamples.Count);

        Assert.Equal(expectedNoncompliantCount, cases.Count(c => c.Variant == RuleExampleVariant.Noncompliant));
    }

    [Fact]
    public void Build_ProducesACompliantCaseOnlyWhenTheExampleHasOne()
    {
        var cases = RuleExampleCorpus.Build();
        var expectedCompliantCount = RuleDocCatalog.ByRuleId.Values
            .SelectMany(c => c.AllExamples)
            .Count(e => e.CompliantSql is not null);

        Assert.Equal(expectedCompliantCount, cases.Count(c => c.Variant == RuleExampleVariant.Compliant));
    }

    [Fact]
    public void Build_TaggsANoncompliantCaseWithNoObjectDefinitionAsNotSelfContained()
    {
        var catalog = new Dictionary<string, RuleDocContent>(StringComparer.Ordinal)
        {
            ["rule/bare-query"] = new RuleDocContent(
                WhyItMatters: "test",
                Examples: [new RuleDocExample("bare query, no DDL", "SELECT 1;")]),
        };

        var cases = RuleExampleCorpus.Build(catalog);

        var single = Assert.Single(cases);
        Assert.False(single.IsSelfContained);
    }

    [Fact]
    public void Build_ExtractsTheDdlPreludeForTheCompliantCase()
    {
        var catalog = new Dictionary<string, RuleDocContent>(StringComparer.Ordinal)
        {
            ["rule/with-prelude"] = new RuleDocContent(
                WhyItMatters: "test",
                Examples:
                [
                    new RuleDocExample(
                        Title: "column wrapped",
                        NoncompliantSql: "CREATE TABLE dbo.T (Id INT NOT NULL);\nGO\nSELECT * FROM dbo.T WHERE Id + 0 = 1;",
                        CompliantSql: "SELECT * FROM dbo.T WHERE Id = 1;"),
                ]),
        };

        var cases = RuleExampleCorpus.Build(catalog);

        var compliant = Assert.Single(cases, c => c.Variant == RuleExampleVariant.Compliant);
        Assert.True(compliant.IsSelfContained);
        Assert.Contains("CREATE TABLE dbo.T", compliant.DeployableSql, StringComparison.Ordinal);
        Assert.Contains("SELECT * FROM dbo.T WHERE Id = 1;", compliant.DeployableSql, StringComparison.Ordinal);
        Assert.DoesNotContain("Id + 0", compliant.DeployableSql, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_ExtractsThePreludeWhenDdlAndTheOffendingQueryShareOneUnGoedBatch()
    {
        var catalog = new Dictionary<string, RuleDocContent>(StringComparer.Ordinal)
        {
            ["rule/no-go-separator"] = new RuleDocContent(
                WhyItMatters: "test",
                Examples:
                [
                    new RuleDocExample(
                        Title: "column wrapped, no GO before the query",
                        NoncompliantSql: "CREATE TABLE dbo.T (Id INT NOT NULL);\nCREATE INDEX IX_T_Id ON dbo.T(Id);\nSELECT * FROM dbo.T WHERE Id + 0 = 1;",
                        CompliantSql: "SELECT * FROM dbo.T WHERE Id = 1;"),
                ]),
        };

        var cases = RuleExampleCorpus.Build(catalog);

        var compliant = Assert.Single(cases, c => c.Variant == RuleExampleVariant.Compliant);
        Assert.True(compliant.IsSelfContained);
        Assert.Contains("CREATE TABLE dbo.T", compliant.DeployableSql, StringComparison.Ordinal);
        Assert.Contains("CREATE INDEX IX_T_Id", compliant.DeployableSql, StringComparison.Ordinal);
    }
}
