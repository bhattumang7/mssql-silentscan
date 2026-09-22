using SilentScan.Verify;
using SilentScan.Verify.Oracle;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class CaseMergeMatrixGeneratorTests
{
    private static readonly SqlServerOptions Options = SqlServerOptions.LocalDocker;

    [Fact]
    public async Task RunAsync_DecimalSpecs_PredictedMergeMatchesEngine()
    {
        var generator = new CaseMergeMatrixGenerator(Options);
        var mismatches = await generator.RunAsync(CaseMergeSpecs.DecimalSpecs);

        Assert.True(mismatches.Count == 0, DescribeMismatches(mismatches));
    }

    [Fact]
    public async Task RunAsync_StringSpecs_PredictedMergeMatchesEngine()
    {
        var generator = new CaseMergeMatrixGenerator(Options);
        var mismatches = await generator.RunAsync(CaseMergeSpecs.StringSpecs);

        Assert.True(mismatches.Count == 0, DescribeMismatches(mismatches));
    }

    [Fact]
    public async Task RunAsync_ExactNumericSpecs_PredictedMergeMatchesEngine()
    {
        var generator = new CaseMergeMatrixGenerator(Options);
        var mismatches = await generator.RunAsync(CaseMergeSpecs.ExactNumericSpecs);

        Assert.True(mismatches.Count == 0, DescribeMismatches(mismatches));
    }

    [Fact]
    public async Task RunAsync_DateTimeSpecs_PredictedMergeMatchesEngine()
    {
        var generator = new CaseMergeMatrixGenerator(Options);
        var mismatches = await generator.RunAsync(CaseMergeSpecs.DateTimeSpecs);

        Assert.True(mismatches.Count == 0, DescribeMismatches(mismatches));
    }

    [Fact]
    public async Task RunAsync_BinarySpecs_PredictedMergeMatchesEngine()
    {
        var generator = new CaseMergeMatrixGenerator(Options);
        var mismatches = await generator.RunAsync(CaseMergeSpecs.BinarySpecs);

        Assert.True(mismatches.Count == 0, DescribeMismatches(mismatches));
    }

    [Fact]
    public async Task RunAsync_AllTypeSpecs_PredictedMergeMatchesEngine()
    {
        var generator = new CaseMergeMatrixGenerator(Options);
        var mismatches = await generator.RunAsync(CaseMergeSpecs.AllTypeSpecs);

        Assert.True(mismatches.Count == 0, DescribeMismatches(mismatches));
    }

    private static string DescribeMismatches(IReadOnlyList<CaseMergeMismatch> mismatches) =>
        string.Join(
            Environment.NewLine,
            mismatches.Select(m => $"{m.LeftSyntax} vs {m.RightSyntax}: predicted={m.Predicted}, actual={m.Actual}"));
}
