using SilentScan.Core.Catalog;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;

namespace SilentScan.Tests.Predicates;

public sealed class ModuleIdentityQualificationTests
{
    [Fact]
    public void ControlFlowRiskScanner_UnqualifiedProcedureName_DefaultsToDboSchema()
    {
        var result = SqlScriptParser.ParseText("test.sql", """
            CREATE PROCEDURE UnqualifiedProc AS
            BEGIN
                SELECT @@IDENTITY;
            END
            """);

        var finding = Assert.Single(
            ControlFlowRiskScanner.Scan(result, new DatabaseCatalog()), f => f.Kind == ControlFlowRiskFindingKind.LegacyIdentityIntrinsic);
        Assert.Equal("dbo.UnqualifiedProc", finding.ModuleQualifiedName);
    }
}
