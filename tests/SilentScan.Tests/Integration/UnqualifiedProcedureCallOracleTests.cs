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

    private static string CalleeWithDefaultSchemas(IReadOnlyCollection<string> defaultSchemas)
    {
        var result = SqlScriptParser.ParseText("calls.sql", Setup);
        var catalog = CatalogBuilder.Build([result]);
        ModuleSchemaNameBinding.BindProcedureCalls(result, ModuleSchemaNameBinding.ProcedureNames([result], catalog.IdentifierComparer), catalog.IdentifierComparer, defaultSchemas);
        var graph = ProcCallGraphBuilder.Build([result], catalog, new SkipLedger());
        return graph.Edges.SingleOrDefault(e => e.CallerScopeQualifiedName == "dbo.caller_in_both")?.CalleeQualifiedName ?? "none";
    }

    [Fact]
    public async Task ProcedureExistingInTwoSchemas_FollowsTheCallersDefaultSchema_SoItIsOnlyBoundWhenEveryCallerAgrees()
    {
        Assert.Equal("dbo", await ScalarAsync<string>("DECLARE @t TABLE (src VARCHAR(10)); INSERT @t EXEC dbo.in_both; SELECT TOP 1 src FROM @t;"));
        Assert.Equal("sa", await ScalarAsync<string>("EXECUTE AS USER = 'u_sa'; DECLARE @t TABLE (src VARCHAR(10)); INSERT @t EXEC dbo.caller_in_both; REVERT; SELECT TOP 1 src FROM @t;"));

        Assert.Equal("dbo.in_both", CalleeWithDefaultSchemas(["dbo"]));
        Assert.Equal("sa.in_both", CalleeWithDefaultSchemas(["sa"]));
        Assert.NotEqual("dbo.in_both", CalleeWithDefaultSchemas(["dbo", "sa"]));
    }

    [Fact]
    public void ProcedureExistingInTwoSchemas_WithoutKnownDefaultSchemas_StaysOnDbo()
    {
        Assert.Equal("dbo.in_both", CalleeOfOnlyEdge("dbo.caller_in_both"));
    }
}
