using SilentScan.Tests.Support;
using SilentScan.Live.Sweep;

namespace SilentScan.Tests.Sweep;

public sealed class ModuleBatchNormalizerTests
{
    private static List<string> Batches(string sql) =>
        [.. SqlBatchText.SplitBatches(ModuleBatchNormalizer.Normalize(sql)).Select(b => b.Trim()).Where(b => b.Length > 0)];

    [Fact]
    public void Normalize_TriggerAfterTable_GetsItsOwnBatch()
    {
        var batches = Batches("CREATE TABLE dbo.T (Id INT);\nCREATE TRIGGER dbo.tr ON dbo.T AFTER INSERT AS SELECT 1;");

        Assert.Equal(2, batches.Count);
        Assert.StartsWith("CREATE TABLE", batches[0], StringComparison.Ordinal);
        Assert.StartsWith("CREATE TRIGGER", batches[1], StringComparison.Ordinal);
    }

    [Fact]
    public void Normalize_CreateOrAlterViewAfterAnotherStatement_GetsItsOwnBatch()
    {
        var batches = Batches("CREATE TABLE dbo.T (Id INT);\nCREATE OR ALTER VIEW dbo.V AS SELECT Id FROM dbo.T;");

        Assert.Equal(2, batches.Count);
        Assert.StartsWith("CREATE OR ALTER VIEW", batches[1], StringComparison.Ordinal);
    }

    [Fact]
    public void Normalize_ModuleAlreadyFirstInItsBatch_IsLeftAlone()
    {
        var batches = Batches("CREATE PROCEDURE dbo.P AS SELECT 1;\nGO\nCREATE FUNCTION dbo.F() RETURNS INT AS BEGIN RETURN 1; END;");

        Assert.Equal(2, batches.Count);
    }

    [Fact]
    public void Normalize_NonModuleCreateStatements_AreNotSplit()
    {
        var batches = Batches("CREATE TABLE dbo.T (Id INT);\nCREATE INDEX IX_T ON dbo.T (Id);");

        Assert.Single(batches);
    }

    [Fact]
    public void Normalize_ModuleKeywordsInsideStringLiteral_AreNotSplit()
    {
        var batches = Batches("CREATE TABLE dbo.T (Id INT);\nSELECT 'CREATE VIEW dbo.V AS SELECT 1';");

        Assert.Single(batches);
    }
}
