using SilentScan.Core.Lineage;
using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;
using SilentScan.Core.TypeInference;
using SilentScan.Verify.Oracle;

namespace SilentScan.Tests.Oracle;

public sealed class ModulePlanComparerTests
{
    private static readonly ColumnProvenance.BaseColumn Provenance =
        new("dbo.T", "Col", new SqlType(SqlTypeCategory.VarChar, Length: 10), Depth: 0);

    private static TypedPredicateFinding Finding(
        Verdict verdict, int line, string table = "dbo.Orders", string column = "Code", SourceSpan? callSite = null)
    {
        var col = new PredicateOperand.Column(table, column, new SqlType(SqlTypeCategory.VarChar, Length: 10), Indexed: true, Depth: 0, Provenance);
        var other = new PredicateOperand.Value(new SqlType(SqlTypeCategory.Int));
        return new TypedPredicateFinding(verdict, col, other, "=", "mod.sql", line, 1, callSite);
    }

    private static ConvertImplicitFinding Conversion(string table = "Orders", string column = "Code", string schema = "dbo") =>
        new("db", schema, table, column, "int", RangeSeekBound: false);

    private static PlannedStatement Statement(int? start, int? end, params ConvertImplicitFinding[] conversions) =>
        new("SELECT 1", start, end, conversions);

    [Fact]
    public void Compare_FindingInsideStatementWithMatchingConversion_Agrees()
    {
        var finding = Finding(Verdict.ScanForced, line: 5);

        var rows = ModulePlanComparer.Compare([finding], [Statement(4, 6, Conversion())]);

        var row = Assert.Single(rows);
        Assert.Equal(ModuleComparisonOutcome.Agree, row.Outcome);
        Assert.Equal(ModuleNotDecidedReason.None, row.Reason);
        Assert.Equal("Orders", row.Table);
        Assert.Equal("Code", row.Column);
        Assert.Equal(4, row.StatementStartLine);
        Assert.Same(finding, row.Finding);
    }

    [Fact]
    public void Compare_SchemaLessFindingAndCaseDifferences_StillAgrees()
    {
        var finding = Finding(Verdict.RangeSeek, line: 5, table: "ORDERS", column: "code");

        var rows = ModulePlanComparer.Compare([finding], [Statement(5, 5, Conversion(schema: "sales"))]);

        Assert.Equal(ModuleComparisonOutcome.Agree, Assert.Single(rows).Outcome);
    }

    [Fact]
    public void Compare_FindingInsideStatementWithoutConversion_IsToolOnly()
    {
        var finding = Finding(Verdict.ScanForced, line: 5);

        var rows = ModulePlanComparer.Compare([finding], [Statement(4, 6)]);

        var row = Assert.Single(rows);
        Assert.Equal(ModuleComparisonOutcome.ToolOnly, row.Outcome);
        Assert.Same(finding, row.Finding);
    }

    [Fact]
    public void Compare_ConversionOnDifferentColumn_IsToolOnlyPlusEngineOnly()
    {
        var finding = Finding(Verdict.ScanForced, line: 5);

        var rows = ModulePlanComparer.Compare([finding], [Statement(4, 6, Conversion(column: "Other"))]);

        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.Outcome == ModuleComparisonOutcome.ToolOnly && r.Column == "Code");
        Assert.Contains(rows, r => r.Outcome == ModuleComparisonOutcome.EngineOnly && r.Column == "Other");
    }

    [Fact]
    public void Compare_SchemaQualifiedFindingWithOtherSchemaConversion_IsToolOnly()
    {
        var finding = Finding(Verdict.ScanForced, line: 5, table: "dbo.Orders");

        var rows = ModulePlanComparer.Compare([finding], [Statement(4, 6, Conversion(schema: "sales"))]);

        Assert.Contains(rows, r => r.Outcome == ModuleComparisonOutcome.ToolOnly);
    }

    [Fact]
    public void Compare_ConversionWithoutFindingInStatement_IsEngineOnly()
    {
        var rows = ModulePlanComparer.Compare([], [Statement(1, 3, Conversion())]);

        var row = Assert.Single(rows);
        Assert.Equal(ModuleComparisonOutcome.EngineOnly, row.Outcome);
        Assert.Equal("Orders", row.Table);
        Assert.Equal("Code", row.Column);
        Assert.Equal(1, row.StatementStartLine);
        Assert.Null(row.Finding);
    }

    [Fact]
    public void Compare_FindingInDifferentStatementThanConversion_IsToolOnlyAndEngineOnly()
    {
        var finding = Finding(Verdict.ScanForced, line: 2);

        var rows = ModulePlanComparer.Compare([finding], [Statement(1, 3), Statement(5, 7, Conversion())]);

        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.Outcome == ModuleComparisonOutcome.ToolOnly && r.StatementStartLine == 1);
        Assert.Contains(rows, r => r.Outcome == ModuleComparisonOutcome.EngineOnly && r.StatementStartLine == 5);
    }

    [Fact]
    public void Compare_DynamicSqlFinding_IsNotDecidedEvenWhenStatementHasConversion()
    {
        var finding = Finding(Verdict.ScanForced, line: 5, callSite: new SourceSpan("mod.sql", 5, 1));

        var rows = ModulePlanComparer.Compare([finding], [Statement(4, 6, Conversion())]);

        var row = rows.Single(r => r.Finding is not null);
        Assert.Equal(ModuleComparisonOutcome.NotDecided, row.Outcome);
        Assert.Equal(ModuleNotDecidedReason.DynamicSql, row.Reason);
    }

    [Fact]
    public void Compare_FindingOutsideEveryLocatedStatement_IsStatementNotLocated()
    {
        var finding = Finding(Verdict.ScanForced, line: 20);

        var rows = ModulePlanComparer.Compare([finding], [Statement(1, 3), Statement(null, null)]);

        var row = Assert.Single(rows);
        Assert.Equal(ModuleComparisonOutcome.NotDecided, row.Outcome);
        Assert.Equal(ModuleNotDecidedReason.StatementNotLocated, row.Reason);
        Assert.Same(finding, row.Finding);
    }

    [Fact]
    public void Compare_OnlyUnlocatedStatements_IsStatementNotLocatedAndIgnoresTheirConversions()
    {
        var finding = Finding(Verdict.ScanForced, line: 2);

        var rows = ModulePlanComparer.Compare([finding], [Statement(null, null, Conversion())]);

        var row = Assert.Single(rows);
        Assert.Equal(ModuleNotDecidedReason.StatementNotLocated, row.Reason);
    }

    [Fact]
    public void Compare_TwoFindingsAndTwoConversionsOnSameColumn_YieldOneRow()
    {
        var findings = new[] { Finding(Verdict.ScanForced, line: 4), Finding(Verdict.RangeSeek, line: 5) };

        var rows = ModulePlanComparer.Compare(findings, [Statement(4, 6, Conversion(), Conversion())]);

        Assert.Equal(ModuleComparisonOutcome.Agree, Assert.Single(rows).Outcome);
    }

    [Fact]
    public void Compare_SeekPreservedAndUnknownFindings_AreIgnored()
    {
        var findings = new[] { Finding(Verdict.SeekPreserved, line: 5), Finding(Verdict.Unknown, line: 5) };

        var rows = ModulePlanComparer.Compare(findings, [Statement(4, 6)]);

        Assert.Empty(rows);
    }

    [Fact]
    public void Compare_SeekPreservedFindingWithEngineConversion_IsEngineOnly()
    {
        var rows = ModulePlanComparer.Compare([Finding(Verdict.SeekPreserved, line: 5)], [Statement(4, 6, Conversion())]);

        var row = Assert.Single(rows);
        Assert.Equal(ModuleComparisonOutcome.EngineOnly, row.Outcome);
        Assert.Null(row.Finding);
    }
}
