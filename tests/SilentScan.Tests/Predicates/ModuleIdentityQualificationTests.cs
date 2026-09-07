using SilentScan.Core.Catalog;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;

namespace SilentScan.Tests.Predicates;

public sealed class ModuleIdentityQualificationTests
{
    [Fact]
    public void DeadCodeScanner_UnqualifiedProcedureName_DefaultsToDboSchema()
    {
        var result = SqlScriptParser.ParseText("test.sql", """
            CREATE PROCEDURE UnqualifiedProc AS BEGIN
                RETURN;
                SELECT 1;
            END
            """);

        var finding = Assert.Single(DeadCodeScanner.Scan(result), f => f.Kind == DeadCodeFindingKind.UnreachableCode);
        Assert.Equal("dbo.UnqualifiedProc", finding.ModuleQualifiedName);
    }

    [Fact]
    public void ControlFlowRiskScanner_UnqualifiedProcedureName_DefaultsToDboSchema()
    {
        var result = SqlScriptParser.ParseText("test.sql", """
            CREATE PROCEDURE UnqualifiedProc AS
            BEGIN
                BEGIN TRY
                    SELECT 1;
                END TRY
                BEGIN CATCH
                END CATCH
            END
            """);

        var finding = Assert.Single(
            ControlFlowRiskScanner.Scan(result, new DatabaseCatalog()), f => f.Kind == ControlFlowRiskFindingKind.EmptyCatchBlock);
        Assert.Equal("dbo.UnqualifiedProc", finding.ModuleQualifiedName);
    }

    [Fact]
    public void DuplicationScanner_UnqualifiedProcedureName_DefaultsToDboSchema()
    {
        var result = SqlScriptParser.ParseText("test.sql", """
            CREATE PROCEDURE UnqualifiedProc AS BEGIN
                SET @x = @x;
            END
            """);

        var finding = Assert.Single(
            DuplicationScanner.Scan(result, CatalogBuilder.Build([result])), f => f.Kind == DuplicationFindingKind.SelfAssignment);
        Assert.Equal("dbo.UnqualifiedProc", finding.ModuleQualifiedName);
    }

    [Fact]
    public void DeprecatedSyntaxScanner_UnqualifiedNumberedProcedureName_DefaultsToDboSchema()
    {
        var result = SqlScriptParser.ParseText("test.sql", "CREATE PROCEDURE UnqualifiedProc;1 AS SELECT 1;");

        var finding = Assert.Single(
            DeprecatedSyntaxScanner.Scan(result), f => f.Kind == DeprecatedSyntaxFindingKind.NumberedProcedureDefinition);
        Assert.Contains("dbo.UnqualifiedProc", finding.DetailText, StringComparison.Ordinal);
    }
}
