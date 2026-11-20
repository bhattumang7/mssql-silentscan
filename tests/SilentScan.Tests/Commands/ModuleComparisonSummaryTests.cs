using SilentScan.Cli.Commands;
using SilentScan.Core.Lineage;
using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;
using SilentScan.Core.TypeInference;
using SilentScan.Verify.Oracle;

namespace SilentScan.Tests.Commands;

public sealed class ModuleComparisonSummaryTests
{
    private static readonly System.Text.Json.JsonSerializerOptions UnescapedJson = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static TypedPredicateFinding Finding(Verdict verdict, SqlTypeCategory column, SqlTypeCategory other) =>
        new(
            verdict,
            new PredicateOperand.Column("dbo.T", "C", new SqlType(column), true, 0, new ColumnProvenance.BaseColumn("dbo.T", "C", new SqlType(column))),
            new PredicateOperand.Value(new SqlType(other)),
            "=",
            "f.sql",
            1,
            1);

    private static ModuleComparisonRow Row(
        ModuleComparisonOutcome outcome, ModuleNotDecidedReason reason = ModuleNotDecidedReason.None, TypedPredicateFinding? finding = null) =>
        new(outcome, reason, "dbo.T", "C", 10, finding);

    private static readonly IReadOnlyList<ModuleComparisonRow> Sample =
    [
        Row(ModuleComparisonOutcome.Agree, finding: Finding(Verdict.ScanForced, SqlTypeCategory.VarChar, SqlTypeCategory.NVarChar)),
        Row(ModuleComparisonOutcome.ToolOnly, finding: Finding(Verdict.ScanForced, SqlTypeCategory.VarChar, SqlTypeCategory.NVarChar)),
        Row(ModuleComparisonOutcome.ToolOnly, finding: Finding(Verdict.RangeSeek, SqlTypeCategory.VarChar, SqlTypeCategory.NVarChar)),
        Row(ModuleComparisonOutcome.ToolOnly, finding: Finding(Verdict.ScanForced, SqlTypeCategory.Int, SqlTypeCategory.BigInt)),
        Row(ModuleComparisonOutcome.EngineOnly),
        Row(ModuleComparisonOutcome.NotDecided, ModuleNotDecidedReason.DynamicSql),
        Row(ModuleComparisonOutcome.NotDecided, ModuleNotDecidedReason.DynamicSql),
        Row(ModuleComparisonOutcome.NotDecided, ModuleNotDecidedReason.StatementNotLocated),
    ];

    [Fact]
    public void Render_ReportsCountsPerOutcomeOverEveryRow()
    {
        var text = ModuleComparisonSummary.Render(Sample, toolOnlyLimit: 25);

        Assert.Contains("total: 8", text, StringComparison.Ordinal);
        Assert.Contains("agree: 1", text, StringComparison.Ordinal);
        Assert.Contains("tool only (not seen in plan): 3", text, StringComparison.Ordinal);
        Assert.Contains("engine only (not flagged by tool): 1", text, StringComparison.Ordinal);
        Assert.Contains("not decided: 3", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_ListsOnlyNonZeroNotDecidedReasons()
    {
        var text = ModuleComparisonSummary.Render(Sample, toolOnlyLimit: 25);

        Assert.Contains("not decided by reason:", text, StringComparison.Ordinal);
        Assert.Contains("  DynamicSql: 2", text, StringComparison.Ordinal);
        Assert.Contains("  StatementNotLocated: 1", text, StringComparison.Ordinal);
        Assert.DoesNotContain("PlanRequestFailed", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ModuleKindNotPlannable", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_GroupsToolOnlyByTypePairSortedByCountThenName()
    {
        var text = ModuleComparisonSummary.Render(Sample, toolOnlyLimit: 25);

        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        var start = lines.IndexOf("tool only by type pair:");
        Assert.True(start >= 0);
        Assert.Equal("  VarChar -> NVarChar: 2", lines[start + 1]);
        Assert.Equal("  Int -> BigInt: 1", lines[start + 2]);
    }

    [Fact]
    public void Render_NeverPrintsObjectNames()
    {
        var text = ModuleComparisonSummary.Render(Sample, toolOnlyLimit: 25);

        Assert.DoesNotContain("dbo.T", text, StringComparison.Ordinal);
        Assert.DoesNotContain("f.sql", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_ListsToolOnlyDetailLinesUpToLimit()
    {
        var text = ModuleComparisonSummary.Render(Sample, toolOnlyLimit: 2);

        var detail = text.Split('\n').Where(l => l.StartsWith("tool-only ", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, detail.Count);
        Assert.Equal("tool-only VarChar -> NVarChar op '=' ScanForced", detail[0].TrimEnd('\r'));
        Assert.Equal("tool-only VarChar -> NVarChar op '=' RangeSeek", detail[1].TrimEnd('\r'));
    }

    [Fact]
    public void Render_OmitsEmptyBlocks()
    {
        IReadOnlyList<ModuleComparisonRow> rows =
        [
            Row(ModuleComparisonOutcome.Agree, finding: Finding(Verdict.ScanForced, SqlTypeCategory.Int, SqlTypeCategory.BigInt)),
        ];

        var text = ModuleComparisonSummary.Render(rows, toolOnlyLimit: 25);

        Assert.Contains("total: 1", text, StringComparison.Ordinal);
        Assert.DoesNotContain("not decided by reason:", text, StringComparison.Ordinal);
        Assert.DoesNotContain("tool only by type pair:", text, StringComparison.Ordinal);
        Assert.DoesNotContain("tool-only ", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ToJsonRows_UsesRowNamesWhenFindingIsNull()
    {
        IReadOnlyList<ModuleComparisonRow> rows =
        [
            Row(ModuleComparisonOutcome.EngineOnly),
        ];

        var json = System.Text.Json.JsonSerializer.Serialize(ModuleComparisonSummary.ToJsonRows(rows).ToList(), UnescapedJson);

        Assert.Contains("\"Outcome\":\"EngineOnly\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Reason\":\"None\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Table\":\"dbo.T\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Column\":\"C\"", json, StringComparison.Ordinal);
        Assert.Contains("\"TypePair\":null", json, StringComparison.Ordinal);
        Assert.Contains("\"StatementStartLine\":10", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ToJsonRows_UsesFindingValuesWhenPresent()
    {
        IReadOnlyList<ModuleComparisonRow> rows =
        [
            Row(ModuleComparisonOutcome.ToolOnly, finding: Finding(Verdict.ScanForced, SqlTypeCategory.VarChar, SqlTypeCategory.NVarChar)),
        ];

        var json = System.Text.Json.JsonSerializer.Serialize(ModuleComparisonSummary.ToJsonRows(rows).ToList(), UnescapedJson);

        Assert.Contains("\"TypePair\":\"VarChar -> NVarChar\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Operator\":\"=\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Verdict\":\"ScanForced\"", json, StringComparison.Ordinal);
        Assert.Contains("\"SourcePath\":\"f.sql\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Line\":1", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_GroupsEngineOnlyRowsByWhereTheConversionSits()
    {
        IReadOnlyList<ModuleComparisonRow> rows =
        [
            new(ModuleComparisonOutcome.EngineOnly, ModuleNotDecidedReason.None, "dbo.T", "C", 1, null, "int", "Predicate"),
            new(ModuleComparisonOutcome.EngineOnly, ModuleNotDecidedReason.None, "dbo.T", "D", 1, null, "int", "Predicate"),
            new(ModuleComparisonOutcome.EngineOnly, ModuleNotDecidedReason.None, "dbo.T", "E", 1, null, "int", "DefinedValue"),
        ];

        var text = ModuleComparisonSummary.Render(rows, toolOnlyLimit: 25);

        Assert.Contains("engine only by where in the plan the conversion sits:", text, StringComparison.Ordinal);
        Assert.Contains("  Predicate: 2", text, StringComparison.Ordinal);
        Assert.Contains("  DefinedValue: 1", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_BreaksPredicateEngineOnlyRowsDownByWhatTheToolSaidAboutThatColumn()
    {
        var seekPreserved = Finding(Verdict.SeekPreserved, SqlTypeCategory.VarChar, SqlTypeCategory.VarChar);
        IReadOnlyList<ModuleComparisonRow> rows =
        [
            new(ModuleComparisonOutcome.EngineOnly, ModuleNotDecidedReason.None, "dbo.T", "C", 1, null, "int", "Predicate"),
            new(ModuleComparisonOutcome.EngineOnly, ModuleNotDecidedReason.None, "dbo.T", "D", 1, seekPreserved, "int", "Predicate"),
            new(ModuleComparisonOutcome.EngineOnly, ModuleNotDecidedReason.None, "dbo.T", "E", 1, null, "int", "DefinedValue"),
        ];

        var text = ModuleComparisonSummary.Render(rows, toolOnlyLimit: 25);

        Assert.Contains("engine only inside a predicate, by what the tool said about that column in that statement:", text, StringComparison.Ordinal);
        Assert.Contains("  no finding: 1", text, StringComparison.Ordinal);
        Assert.Contains("  SeekPreserved: 1", text, StringComparison.Ordinal);
    }
}
