using SilentScan.Core.Catalog;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;

namespace SilentScan.Tests.Predicates;

public sealed class DeprecatedSyntaxScannerTests
{
    private static IReadOnlyList<DeprecatedSyntaxFinding> Scan(string sql)
    {
        var result = SqlScriptParser.ParseText("test.sql", sql);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        return DeprecatedSyntaxScanner.Scan(result);
    }

    [Fact]
    public void EqualsNull_Fires()
    {
        var findings = Scan("SELECT * FROM dbo.T WHERE Col = NULL;");

        Assert.Contains(findings, f => f.Kind == DeprecatedSyntaxFindingKind.EqualsNullComparison);
    }

    [Fact]
    public void EqualsNull_ModuleUsesAnsiNullsOff_Suppressed()
    {
        var result = SqlScriptParser.ParseText("test.sql", "CREATE PROCEDURE dbo.usp_Find AS SELECT * FROM dbo.T WHERE Col = NULL;");
        Assert.False(result.HasErrors);

        var catalog = new DatabaseCatalog();
        catalog.AddModuleUsesAnsiNulls("dbo.usp_Find", usesAnsiNulls: false);
        var findings = DeprecatedSyntaxScanner.Scan(result, catalog);

        Assert.DoesNotContain(findings, f => f.Kind is DeprecatedSyntaxFindingKind.EqualsNullComparison or DeprecatedSyntaxFindingKind.NotEqualsNullComparison);
    }

    [Fact]
    public void EqualsNull_ModuleUsesAnsiNullsTrue_StillFires()
    {
        var result = SqlScriptParser.ParseText("test.sql", "CREATE PROCEDURE dbo.usp_Find AS SELECT * FROM dbo.T WHERE Col = NULL;");
        Assert.False(result.HasErrors);

        var catalog = new DatabaseCatalog();
        catalog.AddModuleUsesAnsiNulls("dbo.usp_Find", usesAnsiNulls: true);
        var findings = DeprecatedSyntaxScanner.Scan(result, catalog);

        Assert.Contains(findings, f => f.Kind == DeprecatedSyntaxFindingKind.EqualsNullComparison);
    }

    [Fact]
    public void EqualsNull_ModuleFlagUnresolved_StillFires()
    {

        var result = SqlScriptParser.ParseText("test.sql", "CREATE PROCEDURE dbo.usp_Find AS SELECT * FROM dbo.T WHERE Col = NULL;");
        Assert.False(result.HasErrors);

        var findings = DeprecatedSyntaxScanner.Scan(result, catalog: new DatabaseCatalog());

        Assert.Contains(findings, f => f.Kind == DeprecatedSyntaxFindingKind.EqualsNullComparison);
    }

    [Fact]
    public void EqualsNull_AdHocScriptAfterSetAnsiNullsOff_Suppressed()
    {
        var findings = Scan("SET ANSI_NULLS OFF; SELECT * FROM dbo.T WHERE Col = NULL;");

        Assert.DoesNotContain(findings, f => f.Kind is DeprecatedSyntaxFindingKind.EqualsNullComparison or DeprecatedSyntaxFindingKind.NotEqualsNullComparison);
    }

    [Fact]
    public void EqualsNull_AdHocScriptBeforeSetAnsiNullsOff_StillFires()
    {
        var findings = Scan("SELECT * FROM dbo.T WHERE Col = NULL; SET ANSI_NULLS OFF;");

        Assert.Contains(findings, f => f.Kind == DeprecatedSyntaxFindingKind.EqualsNullComparison);
    }

    [Fact]
    public void EqualsNull_AdHocScriptAfterSetAnsiNullsBackOn_StillFires()
    {
        var findings = Scan("SET ANSI_NULLS OFF; SET ANSI_NULLS ON; SELECT * FROM dbo.T WHERE Col = NULL;");

        Assert.Contains(findings, f => f.Kind == DeprecatedSyntaxFindingKind.EqualsNullComparison);
    }

    [Fact]
    public void EqualsNull_SetAnsiNullsOffInsideConditionalBlock_DoesNotPropagatePastBlock()
    {
        var findings = Scan("IF 1 = 1 BEGIN SET ANSI_NULLS OFF; END SELECT * FROM dbo.T WHERE Col = NULL;");

        Assert.Contains(findings, f => f.Kind == DeprecatedSyntaxFindingKind.EqualsNullComparison);
    }

    [Fact]
    public void EqualsNull_SetAnsiNullsOffInsideUnconditionalBeginEndBlock_PropagatesPastBlock()
    {
        var findings = Scan("BEGIN SET ANSI_NULLS OFF; END SELECT * FROM dbo.T WHERE Col = NULL;");

        Assert.DoesNotContain(findings, f => f.Kind == DeprecatedSyntaxFindingKind.EqualsNullComparison);
    }

    [Fact]
    public void EqualsNull_SetAnsiNullsOffEarlierInSameConditionalBranch_Suppressed()
    {
        var findings = Scan("IF 1 = 1 BEGIN SET ANSI_NULLS OFF; SELECT * FROM dbo.T WHERE Col = NULL; END");

        Assert.DoesNotContain(findings, f => f.Kind == DeprecatedSyntaxFindingKind.EqualsNullComparison);
    }

    [Fact]
    public void EqualsNull_SetAnsiNullsOffInElseBranch_DoesNotAffectThenBranch()
    {
        var findings = Scan(
            "IF 1 = 1 BEGIN SELECT * FROM dbo.T WHERE Col = NULL; END ELSE BEGIN SET ANSI_NULLS OFF; END");

        Assert.Contains(findings, f => f.Kind == DeprecatedSyntaxFindingKind.EqualsNullComparison);
    }

    [Fact]
    public void EqualsNull_SetAnsiNullsOffInsideWhileBody_SuppressedWithinBodyButNotAfterLoop()
    {
        var findings = Scan(
            "WHILE 1 = 0 BEGIN SET ANSI_NULLS OFF; SELECT * FROM dbo.T WHERE Col = NULL; END SELECT * FROM dbo.T WHERE Col2 = NULL;");

        Assert.Single(findings, f => f.Kind == DeprecatedSyntaxFindingKind.EqualsNullComparison);
    }

    [Fact]
    public void EqualsNull_SetAnsiNullsOffInsideTryBlock_DoesNotAffectCatchBlock()
    {
        var findings = Scan(
            "BEGIN TRY SET ANSI_NULLS OFF; END TRY BEGIN CATCH SELECT * FROM dbo.T WHERE Col = NULL; END CATCH");

        Assert.Contains(findings, f => f.Kind == DeprecatedSyntaxFindingKind.EqualsNullComparison);
    }

    [Fact]
    public void NotEqualToBracketsNull_Fires()
    {
        var findings = Scan("SELECT * FROM dbo.T WHERE Col <> NULL;");

        Assert.Contains(findings, f => f.Kind == DeprecatedSyntaxFindingKind.NotEqualsNullComparison);
    }

    [Fact]
    public void IsNull_NeverFiresEqualsNull()
    {
        var findings = Scan("SELECT * FROM dbo.T WHERE Col IS NULL;");

        Assert.DoesNotContain(findings, f => f.Kind is DeprecatedSyntaxFindingKind.EqualsNullComparison or DeprecatedSyntaxFindingKind.NotEqualsNullComparison);
    }

    [Fact]
    public void EqualsRealValue_NeverFiresEqualsNull()
    {
        var findings = Scan("SELECT * FROM dbo.T WHERE Col = 1;");

        Assert.DoesNotContain(findings, f => f.Kind == DeprecatedSyntaxFindingKind.EqualsNullComparison);
    }

}
