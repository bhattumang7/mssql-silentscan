using SilentScan.Core.Catalog;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;

namespace SilentScan.Tests.Predicates;

public sealed class ModuleCompileFlagScannerTests
{
    private const string ModuleName = "dbo.usp_Test";

    private static IReadOnlyList<ModuleCompileFlagFinding> Scan(
        string sql, bool? usesDatabaseCollation = null, bool? isSchemaBound = null)
    {
        var catalog = new DatabaseCatalog();
        var result = SqlScriptParser.ParseText(ModuleName, sql);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));

        if (usesDatabaseCollation is { } udc)
        {
            catalog.AddModuleUsesDatabaseCollation(ModuleName, udc);
        }

        if (isSchemaBound is { } sb)
        {
            catalog.AddModuleIsSchemaBound(ModuleName, sb);
        }

        return ModuleCompileFlagScanner.Scan(result, catalog);
    }

    [Fact]
    public void UsesDatabaseCollationTrue_NonSchemaBound_Fires()
    {
        var findings = Scan(
            "CREATE FUNCTION dbo.usp_Test() RETURNS @t TABLE (Val VARCHAR(50)) AS BEGIN RETURN; END",
            usesDatabaseCollation: true, isSchemaBound: false);

        var finding = Assert.Single(findings);
        Assert.Equal(ModuleCompileFlagFindingKind.TableValuedFunctionReturnUsesDatabaseCollation, finding.Kind);
    }

    [Fact]
    public void UsesDatabaseCollationTrue_SchemaBound_DoesNotFire()
    {

        var findings = Scan(
            "CREATE FUNCTION dbo.usp_Test() RETURNS @t TABLE (Val VARCHAR(50)) AS BEGIN RETURN; END",
            usesDatabaseCollation: true, isSchemaBound: true);

        Assert.Empty(findings);
    }

    [Fact]
    public void UsesDatabaseCollationFalse_DoesNotFire()
    {
        var findings = Scan(
            "CREATE FUNCTION dbo.usp_Test() RETURNS @t TABLE (Val VARCHAR(50) COLLATE SQL_Latin1_General_CP1_CI_AS) AS BEGIN RETURN; END",
            usesDatabaseCollation: false, isSchemaBound: false);

        Assert.Empty(findings);
    }
}
