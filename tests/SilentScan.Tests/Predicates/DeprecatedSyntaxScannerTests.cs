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
    public void TodoComment_Fires()
    {
        var findings = Scan("-- TODO: fix this later\nSELECT 1;");

        Assert.Contains(findings, f => f.Kind == DeprecatedSyntaxFindingKind.TaskCommentTodo);
    }

    [Fact]
    public void FixmeComment_Fires()
    {
        var findings = Scan("/* FIXME - broken under load */\nSELECT 1;");

        Assert.Contains(findings, f => f.Kind == DeprecatedSyntaxFindingKind.TaskCommentFixme);
    }

    [Fact]
    public void TodoAsPartOfLongerWord_NeverFires()
    {
        var findings = Scan("-- see the TODOLIST spreadsheet for the full backlog\nSELECT 1;");

        Assert.DoesNotContain(findings, f => f.Kind == DeprecatedSyntaxFindingKind.TaskCommentTodo);
    }

    [Fact]
    public void OrdinaryComment_NeverFiresTaskComment()
    {
        var findings = Scan("-- computes the running total\nSELECT 1;");

        Assert.DoesNotContain(findings, f => f.Kind is DeprecatedSyntaxFindingKind.TaskCommentTodo or DeprecatedSyntaxFindingKind.TaskCommentFixme);
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

    [Fact]
    public void LegacyCompatibilityView_Fires()
    {
        var findings = Scan("SELECT * FROM sysobjects;");

        Assert.Contains(findings, f => f.Kind == DeprecatedSyntaxFindingKind.LegacySystemCompatibilityView);
    }

    [Fact]
    public void RealCatalogView_NeverFiresLegacyCompatibilityView()
    {
        var findings = Scan("SELECT * FROM sys.objects;");

        Assert.DoesNotContain(findings, f => f.Kind == DeprecatedSyntaxFindingKind.LegacySystemCompatibilityView);
    }

    [Fact]
    public void OrdinaryTableNamedLikeCompatibilityView_NeverFires()
    {
        var findings = Scan("SELECT * FROM app.sysobjects;");

        Assert.DoesNotContain(findings, f => f.Kind == DeprecatedSyntaxFindingKind.LegacySystemCompatibilityView);
    }

    [Fact]
    public void Syslocks_NeverFires()
    {
        var findings = Scan("SELECT * FROM syslocks;");

        Assert.DoesNotContain(findings, f => f.Kind == DeprecatedSyntaxFindingKind.LegacySystemCompatibilityView);
    }

    [Fact]
    public void Syslockinfo_Fires()
    {
        var findings = Scan("SELECT * FROM syslockinfo;");

        Assert.Contains(findings, f => f.Kind == DeprecatedSyntaxFindingKind.LegacySystemCompatibilityView);
    }

    [Fact]
    public void TableHintWithoutWith_Fires()
    {
        var findings = Scan("SELECT * FROM dbo.T (NOLOCK);");

        Assert.Contains(findings, f => f.Kind == DeprecatedSyntaxFindingKind.TableHintWithoutWith);
    }

    [Fact]
    public void TableHintWithWith_NeverFires()
    {
        var findings = Scan("SELECT * FROM dbo.T WITH (NOLOCK);");

        Assert.DoesNotContain(findings, f => f.Kind == DeprecatedSyntaxFindingKind.TableHintWithoutWith);
    }

    [Fact]
    public void NoTableHint_NeverFires()
    {
        var findings = Scan("SELECT * FROM dbo.T;");

        Assert.DoesNotContain(findings, f => f.Kind == DeprecatedSyntaxFindingKind.TableHintWithoutWith);
    }

    [Fact]
    public void NumberedProcedureDefinition_Fires()
    {
        var findings = Scan("CREATE PROCEDURE dbo.Foo;1 AS SELECT 1;");

        Assert.Contains(findings, f => f.Kind == DeprecatedSyntaxFindingKind.NumberedProcedureDefinition);
    }

    [Fact]
    public void OrdinaryProcedureDefinition_NeverFiresNumbered()
    {
        var findings = Scan("CREATE PROCEDURE dbo.Foo AS SELECT 1;");

        Assert.DoesNotContain(findings, f => f.Kind == DeprecatedSyntaxFindingKind.NumberedProcedureDefinition);
    }

    [Fact]
    public void NumberedProcedureExecution_Fires()
    {
        var findings = Scan("EXEC dbo.Foo;1;");

        Assert.Contains(findings, f => f.Kind == DeprecatedSyntaxFindingKind.NumberedProcedureExecution);
    }

    [Fact]
    public void OrdinaryProcedureExecution_NeverFiresNumbered()
    {
        var findings = Scan("EXEC dbo.Foo;");

        Assert.DoesNotContain(findings, f => f.Kind == DeprecatedSyntaxFindingKind.NumberedProcedureExecution);
    }

    [Fact]
    public void SetRowcount_Fires()
    {
        var findings = Scan("SET ROWCOUNT 10;");

        Assert.Contains(findings, f => f.Kind == DeprecatedSyntaxFindingKind.DeprecatedSetRowcount);
    }

    [Fact]
    public void NoSetRowcount_NeverFires()
    {
        var findings = Scan("SELECT TOP (10) * FROM dbo.T;");

        Assert.DoesNotContain(findings, f => f.Kind == DeprecatedSyntaxFindingKind.DeprecatedSetRowcount);
    }

}
