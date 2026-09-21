using SilentScan.Core.Catalog;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;
using SilentScan.Core.Reporting;
using SilentScan.Core.Reporting.RuleDocs;
using SilentScan.Core.Reporting.Sarif;
using SilentScan.Core.TypeInference;

namespace SilentScan.Tests.Predicates;

public sealed class NamingScannerTests
{
    private static IReadOnlyList<NamingFinding> Scan(string sql)
    {
        var result = SqlScriptParser.ParseText("test.sql", sql);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        return NamingScanner.Scan(result);
    }

    private static IReadOnlyList<NamingFinding> ScanWithCatalog(string ddl, string sql)
    {
        var result = SqlScriptParser.ParseText("test.sql", $"{ddl}\nGO\n{sql}");
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));

        var catalog = CatalogBuilder.Build([result]);
        return NamingScanner.Scan(result, catalog);
    }

    [Fact]
    public void UnqualifiedCreateProcedure_Fires()
    {
        var findings = Scan("CREATE PROCEDURE DoSomething AS BEGIN SELECT 1; END");

        Assert.Contains(findings, f => f.Kind == NamingFindingKind.UnqualifiedCreate);
    }

    [Fact]
    public void QualifiedCreateProcedure_NeverFiresUnqualified()
    {
        var findings = Scan("CREATE PROCEDURE dbo.DoSomething AS BEGIN SELECT 1; END");

        Assert.DoesNotContain(findings, f => f.Kind == NamingFindingKind.UnqualifiedCreate);
    }

    [Fact]
    public void UnqualifiedCreateView_Fires()
    {
        var findings = Scan("CREATE VIEW MyView AS SELECT 1 AS Col;");

        Assert.Contains(findings, f => f.Kind == NamingFindingKind.UnqualifiedCreate);
    }

    [Fact]
    public void RedundantDboTypeQualifier_OnParameter_Fires()
    {
        var sql = "CREATE PROCEDURE dbo.P (@p dbo.MyType READONLY) AS BEGIN SELECT 1; END";
        var findings = Scan(sql);

        Assert.Contains(findings, f => f.Kind == NamingFindingKind.RedundantTypeQualifier);
    }

    [Fact]
    public void RedundantDboTypeQualifier_OnDeclare_Fires()
    {
        var findings = Scan("DECLARE @p dbo.MyType;");

        Assert.Contains(findings, f => f.Kind == NamingFindingKind.RedundantTypeQualifier);
    }

    [Fact]
    public void UnqualifiedType_NeverFiresRedundantQualifier()
    {
        var findings = Scan("DECLARE @p MyType;");

        Assert.DoesNotContain(findings, f => f.Kind == NamingFindingKind.RedundantTypeQualifier);
    }

    [Fact]
    public void BuiltInType_NeverFiresRedundantQualifier()
    {
        var findings = Scan("DECLARE @p INT;");

        Assert.DoesNotContain(findings, f => f.Kind == NamingFindingKind.RedundantTypeQualifier);
    }

    [Fact]
    public void NonDboSchemaTypeQualifier_NeverFiresRedundantQualifier()
    {

        var findings = Scan("DECLARE @p custom.MyType;");

        Assert.DoesNotContain(findings, f => f.Kind == NamingFindingKind.RedundantTypeQualifier);
    }

    [Fact]
    public void TableColumn_RedundantDboTypeQualifier_Fires()
    {
        var findings = Scan("CREATE TABLE dbo.T (Id dbo.MyType NOT NULL);");

        Assert.Contains(findings, f => f.Kind == NamingFindingKind.RedundantTypeQualifier);
    }

    [Fact]
    public void RedundantDboTypeQualifier_SuppressedWhenSameNamedTypeExistsInAnotherSchema()
    {
        const string ddl = """
            CREATE TYPE dbo.mytype FROM INT;
            CREATE TYPE alt.mytype FROM VARCHAR(50);
            """;

        var findings = ScanWithCatalog(ddl, "DECLARE @p dbo.mytype;");

        Assert.DoesNotContain(findings, f => f.Kind == NamingFindingKind.RedundantTypeQualifier);
    }

    [Fact]
    public void RedundantDboTypeQualifier_StillFiresWhenTypeNameIsUniqueToDbo()
    {
        const string ddl = "CREATE TYPE dbo.mytype FROM INT;";

        var findings = ScanWithCatalog(ddl, "DECLARE @p dbo.mytype;");

        Assert.Contains(findings, f => f.Kind == NamingFindingKind.RedundantTypeQualifier);
    }

    [Fact]
    public void AlterProcedureUnqualified_Fires()
    {
        var findings = Scan("ALTER PROCEDURE DoSomething AS BEGIN SELECT 1; END");

        Assert.Contains(findings, f => f.Kind == NamingFindingKind.UnqualifiedCreate);
    }

    [Fact]
    public void AlterProcedureQualified_NeverFiresUnqualified()
    {
        var findings = Scan("ALTER PROCEDURE dbo.DoSomething AS BEGIN SELECT 1; END");

        Assert.DoesNotContain(findings, f => f.Kind == NamingFindingKind.UnqualifiedCreate);
    }

    [Fact]
    public void AlterFunctionUnqualified_Fires()
    {
        var findings = Scan("ALTER FUNCTION Calculate() RETURNS INT AS BEGIN RETURN 1; END");

        Assert.Contains(findings, f => f.Kind == NamingFindingKind.UnqualifiedCreate);
    }

    [Fact]
    public void CreateFunctionUnqualified_Fires()
    {
        var findings = Scan("CREATE FUNCTION Calculate() RETURNS INT AS BEGIN RETURN 1; END");

        Assert.Contains(findings, f => f.Kind == NamingFindingKind.UnqualifiedCreate);
    }

    [Fact]
    public void QualifiedCreateFunction_NeverFiresUnqualified()
    {
        var findings = Scan("CREATE FUNCTION dbo.Calculate() RETURNS INT AS BEGIN RETURN 1; END");

        Assert.DoesNotContain(findings, f => f.Kind == NamingFindingKind.UnqualifiedCreate);
    }

    [Fact]
    public void AlterViewUnqualified_Fires()
    {
        var findings = Scan("ALTER VIEW MyView AS SELECT 1 AS Col;");

        Assert.Contains(findings, f => f.Kind == NamingFindingKind.UnqualifiedCreate);
    }

    [Fact]
    public void AlterViewQualified_NeverFiresUnqualified()
    {
        var findings = Scan("ALTER VIEW dbo.MyView AS SELECT 1 AS Col;");

        Assert.DoesNotContain(findings, f => f.Kind == NamingFindingKind.UnqualifiedCreate);
    }

    [Fact]
    public void UnqualifiedCreateProcedure_DetailTextNamesProcedureAndOmitsSchema()
    {
        var findings = Scan("CREATE PROCEDURE DoSomething AS BEGIN SELECT 1; END");

        var finding = Assert.Single(findings, f => f.Kind == NamingFindingKind.UnqualifiedCreate);
        Assert.Equal(
            "Procedure \"DoSomething\" is created with no explicit schema qualifier - its real owning schema depends on the connecting principal's own default schema.",
            finding.DetailText);
    }
}
