using SilentScan.Cli.Commands;
using SilentScan.Core.Lineage;
using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;
using SilentScan.Core.TypeInference;
using SilentScan.Verify.Oracle;

namespace SilentScan.Tests.Commands;

public sealed class CorpusVerificationSummaryTests
{
    private static CorpusFindingResult Result(
        Verdict verdict, SqlTypeCategory column, SqlTypeCategory other, CorpusFindingOutcome outcome, bool? columnConverted = null) =>
        new(
            new TypedPredicateFinding(
                verdict,
                new PredicateOperand.Column("dbo.T", "C", new SqlType(column), true, 0, new ColumnProvenance.BaseColumn("dbo.T", "C", new SqlType(column))),
                new PredicateOperand.Value(new SqlType(other)),
                "=",
                "f.sql",
                1,
                1),
            outcome,
            Detail: null,
            ColumnConverted: columnConverted);

    private static readonly IReadOnlyList<CorpusFindingResult> Sample =
    [
        Result(Verdict.ScanForced, SqlTypeCategory.VarChar, SqlTypeCategory.NVarChar, CorpusFindingOutcome.Confirmed, true),
        Result(Verdict.ScanForced, SqlTypeCategory.VarChar, SqlTypeCategory.NVarChar, CorpusFindingOutcome.ConfirmedUnindexed, true),
        Result(Verdict.RangeSeek, SqlTypeCategory.VarChar, SqlTypeCategory.NVarChar, CorpusFindingOutcome.NotConfirmed, true),
        Result(Verdict.ScanForced, SqlTypeCategory.Int, SqlTypeCategory.BigInt, CorpusFindingOutcome.NotConfirmed, false),
        Result(Verdict.ScanForced, SqlTypeCategory.Int, SqlTypeCategory.BigInt, CorpusFindingOutcome.ProbeFailed),
    ];

    [Fact]
    public void Classify_NotConfirmedWithColumnConversionObserved_IsShapeMismatchNotRefutation()
    {
        Assert.Equal("NotConfirmed:shape-mismatch", CorpusVerificationSummary.Classify(Sample[2]));
        Assert.Equal("NotConfirmed:no-conversion", CorpusVerificationSummary.Classify(Sample[3]));
        Assert.Equal("Confirmed", CorpusVerificationSummary.Classify(Sample[0]));
    }

    [Fact]
    public void Render_ReportsConversionConfirmedAndRefutedCountsOverEveryResult()
    {
        var text = CorpusVerificationSummary.Render(Sample, missLimit: 25);

        Assert.Contains("total: 5", text, StringComparison.Ordinal);
        Assert.Contains("conversion confirmed: 3", text, StringComparison.Ordinal);
        Assert.Contains("conversion refuted: 1", text, StringComparison.Ordinal);
        Assert.Contains("not decided: 1", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_GroupsByTypePairAndNeverPrintsObjectNames()
    {
        var text = CorpusVerificationSummary.Render(Sample, missLimit: 25);

        Assert.Contains("VarChar -> NVarChar", text, StringComparison.Ordinal);
        Assert.Contains("Int -> BigInt", text, StringComparison.Ordinal);
        Assert.DoesNotContain("dbo.T", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_LimitsListedMissesToMissLimit()
    {
        var text = CorpusVerificationSummary.Render(Sample, missLimit: 1);

        Assert.Equal(1, text.Split('\n').Count(l => l.StartsWith("miss ", StringComparison.Ordinal)));
    }

    [Fact]
    public void Classify_NotConfirmedWithoutColumnConversionButWithADynamicRangeSeek_IsItsOwnClass()
    {
        var result = Result(Verdict.RangeSeek, SqlTypeCategory.VarChar, SqlTypeCategory.VarChar, CorpusFindingOutcome.NotConfirmed, false) with { DynamicRangeSeekObserved = true };

        Assert.Equal("NotConfirmed:dynamic-range-seek-without-column-conversion", CorpusVerificationSummary.Classify(result));
    }
}
