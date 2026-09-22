using SilentScan.Tests.Support;
using SilentScan.Core.Reporting;
using SilentScan.Core.Reporting.RuleDocs;
using SilentScan.Live.Sweep;

namespace SilentScan.Tests.Sweep;

public sealed class RuleExampleCorpusTests
{
    [Fact]
    public void Build_ProducesANoncompliantCaseForEveryPublishedExample()
    {
        var cases = RuleExampleCorpus.Build();
        var expectedNoncompliantCount = RuleDocCatalog.ByRuleId
            .Where(entry => !StyleRuleFamilies.IsStyleRule(entry.Key))
            .Sum(entry => entry.Value.AllExamples.Count);

        Assert.Equal(expectedNoncompliantCount, cases.Count(c => c.Variant == RuleExampleVariant.Noncompliant));
    }

    [Fact]
    public void Build_ProducesACompliantCaseOnlyWhenTheExampleHasOne()
    {
        var cases = RuleExampleCorpus.Build();
        var expectedCompliantCount = RuleDocCatalog.ByRuleId
            .Where(entry => !StyleRuleFamilies.IsStyleRule(entry.Key))
            .SelectMany(entry => entry.Value.AllExamples)
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

    [Fact]
    public void Build_CarriesTheLatestEngineRequirementToBothVariants()
    {
        var catalog = new Dictionary<string, RuleDocContent>(StringComparer.Ordinal)
        {
            ["rule/latest-engine"] = new RuleDocContent(
                WhyItMatters: "test",
                Examples:
                [
                    new RuleDocExample(
                        Title: "needs a newer engine",
                        NoncompliantSql: "CREATE TABLE dbo.T (Id INT NOT NULL);\nGO\nSELECT * FROM dbo.T WHERE Id + 0 = 1;",
                        CompliantSql: "SELECT * FROM dbo.T WHERE Id = 1;",
                        RequiresLatestEngine: true),
                ]),
        };

        var cases = RuleExampleCorpus.Build(catalog);

        Assert.Equal(2, cases.Count);
        Assert.All(cases, c => Assert.True(c.RequiresLatestEngine));
    }

    [Fact]
    public void Build_SeparatesTheExtractedPreludeFromTheCompliantSqlWithBatchSeparators()
    {
        var catalog = new Dictionary<string, RuleDocContent>(StringComparer.Ordinal)
        {
            ["rule/separate-batches"] = new RuleDocContent(
                WhyItMatters: "test",
                Examples:
                [
                    new RuleDocExample(
                        Title: "caller declares a variable the prelude's procedure body also declares",
                        NoncompliantSql: "CREATE PROCEDURE dbo.P @A INT AS BEGIN SELECT @A; END;\nGO\nDECLARE @v INT = 1;\nEXEC dbo.P @A = @v;",
                        CompliantSql: "DECLARE @v INT = 2;\nEXEC dbo.P @A = @v;"),
                ]),
        };

        var cases = RuleExampleCorpus.Build(catalog);

        var compliant = Assert.Single(cases, c => c.Variant == RuleExampleVariant.Compliant);
        var batches = SqlBatchText.SplitBatches(compliant.DeployableSql);
        Assert.Equal(2, batches.Length);
        Assert.Contains("CREATE PROCEDURE dbo.P", batches[0], StringComparison.Ordinal);
        Assert.DoesNotContain("DECLARE", batches[0], StringComparison.Ordinal);
        Assert.Contains("DECLARE @v INT = 2", batches[1], StringComparison.Ordinal);
    }

    [Fact]
    public void Build_LeavesTheNoncompliantTriggerOutOfThePreludeWhenTheCompliantSideRedefinesIt()
    {
        var catalog = new Dictionary<string, RuleDocContent>(StringComparer.Ordinal)
        {
            ["rule/redefined-trigger"] = new RuleDocContent(
                WhyItMatters: "test",
                Examples:
                [
                    new RuleDocExample(
                        Title: "trigger redefined by the compliant variant",
                        NoncompliantSql: "CREATE TABLE dbo.T (Id INT);\nGO\nCREATE TRIGGER dbo.trg_T ON dbo.T AFTER INSERT AS SELECT 1;",
                        CompliantSql: "CREATE TRIGGER dbo.trg_T ON dbo.T AFTER INSERT AS SELECT 2;"),
                ]),
        };

        var cases = RuleExampleCorpus.Build(catalog);

        var compliant = Assert.Single(cases, c => c.Variant == RuleExampleVariant.Compliant);
        Assert.Contains("CREATE TABLE dbo.T", compliant.DeployableSql, StringComparison.Ordinal);
        Assert.Equal(1, SqlBatchText.CountCreateTrigger(compliant.DeployableSql));
        Assert.Contains("SELECT 2", compliant.DeployableSql, StringComparison.Ordinal);
    }
}
