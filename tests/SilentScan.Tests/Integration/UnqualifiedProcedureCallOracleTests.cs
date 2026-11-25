using SilentScan.Core.Catalog;
using SilentScan.Core.Diagnostics;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class UnqualifiedProcedureCallOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(UnqualifiedProcedureCallOracleTests);

    private const string Setup = """
        CREATE SCHEMA sa;
        GO
        CREATE PROCEDURE sa.only_in_sa AS SELECT 'sa' AS src;
        GO
        CREATE PROCEDURE dbo.in_both AS SELECT 'dbo' AS src;
        GO
        CREATE PROCEDURE sa.in_both AS SELECT 'sa' AS src;
        GO
        CREATE PROCEDURE dbo.caller_only_in_sa AS EXEC only_in_sa;
        GO
        CREATE PROCEDURE dbo.caller_in_both AS EXEC in_both;
        GO
        """;

    protected override string Ddl => Setup + Environment.NewLine + """
        CREATE USER u_sa WITHOUT LOGIN WITH DEFAULT_SCHEMA = sa;
        GRANT EXECUTE TO u_sa;
        """;

    private static string CalleeOfOnlyEdge(string callerName)
    {
        var result = SqlScriptParser.ParseText("calls.sql", Setup);
        Assert.False(result.HasErrors);
        var catalog = CatalogBuilder.Build([result]);
        ModuleSchemaNameBinding.Bind([result], catalog);
        var graph = ProcCallGraphBuilder.Build([result], catalog, new SkipLedger());
        return graph.Edges.Single(e => e.CallerScopeQualifiedName == callerName).CalleeQualifiedName;
    }

    [Fact]
    public async Task ProcedureExistingInOneSchemaOnly_IsWhatAnUnqualifiedExecReaches_ForTheCallersThatCanReachIt()
    {
        Assert.Equal("sa", await ScalarAsync<string>("EXECUTE AS USER = 'u_sa'; DECLARE @t TABLE (src VARCHAR(10)); INSERT @t EXEC sa.only_in_sa; REVERT; SELECT TOP 1 src FROM @t;"));
        Assert.Equal(2812, await SqlErrorNumberAsync("EXEC dbo.caller_only_in_sa;"));

        Assert.Equal("sa.only_in_sa", CalleeOfOnlyEdge("dbo.caller_only_in_sa"));
    }

    [Fact]
    public void ProcedureExistingInTwoSchemas_StaysOnTheDefaultSchemaBecauseTheCallerDecides()
    {
        Assert.Equal("dbo.in_both", CalleeOfOnlyEdge("dbo.caller_in_both"));
    }
}
