using SilentScan.Core.Catalog;
using SilentScan.Core.Common;
using SilentScan.Core.Parsing;
using SilentScan.Tests.Support;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class DynamicSqlSchemaBindingOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(DynamicSqlSchemaBindingOracleTests);

    private const string Tables = """
        CREATE SCHEMA sa;
        GO
        CREATE TABLE dbo.both_t (src VARCHAR(10) NULL);
        GO
        CREATE TABLE sa.both_t (src VARCHAR(10) NULL);
        GO
        CREATE TABLE sa.sa_only (src VARCHAR(10) NULL);
        GO
        """;

    protected override string Ddl => Tables + Environment.NewLine + """
        INSERT dbo.both_t VALUES ('dbo'); INSERT sa.both_t VALUES ('sa'); INSERT sa.sa_only VALUES ('sa');
        GO
        CREATE PROCEDURE sa.p_dyn AS EXEC('SELECT TOP 1 src FROM both_t');
        GO
        CREATE USER u_sa WITHOUT LOGIN WITH DEFAULT_SCHEMA = sa;
        GRANT EXECUTE TO u_sa;
        GRANT SELECT TO u_sa;
        """;

    private static string BoundTable(string tableName, IReadOnlyCollection<string> defaultSchemas)
    {
        var tables = SqlScriptParser.ParseText("tables.sql", Tables);
        var catalog = CatalogBuilder.Build([tables]);
        var inner = SqlScriptParser.ParseText("dyn.sql", $"SELECT src FROM {tableName}");
        ModuleSchemaNameBinding.BindDynamicSqlTables(inner, catalog, defaultSchemas);
        var reference = new Collector();
        inner.Fragment.Accept(reference);
        return SchemaObjectNameHelper.Qualify(reference.Name!);
    }

    private sealed class Collector : TSqlFragmentVisitor
    {
        public SchemaObjectName? Name { get; private set; }

        public override void Visit(NamedTableReference node) => Name = node.SchemaObject;
    }

    [Fact]
    public async Task UnqualifiedTableInAnExecString_ResolvesThroughTheExecutingUsersDefaultSchema()
    {
        Assert.Equal("dbo", await ScalarAsync<string>("DECLARE @t TABLE (src VARCHAR(10)); INSERT @t EXEC sa.p_dyn; SELECT TOP 1 src FROM @t;"));
        Assert.Equal("sa", await ScalarAsync<string>("EXECUTE AS USER = 'u_sa'; DECLARE @t TABLE (src VARCHAR(10)); INSERT @t EXEC sa.p_dyn; REVERT; SELECT TOP 1 src FROM @t;"));

        Assert.Equal("dbo.both_t", BoundTable("both_t", ["dbo"]));
        Assert.Equal("sa.both_t", BoundTable("both_t", ["sa"]));
        Assert.NotEqual("dbo.both_t", BoundTable("both_t", ["dbo", "sa"]));
        Assert.NotEqual("sa.both_t", BoundTable("both_t", ["dbo", "sa"]));
    }

    [Fact]
    public void TableThatExistsOnlyInTheCallersSchema_IsBoundWhenAllCallersCanReachIt()
    {
        Assert.Equal("sa.sa_only", BoundTable("sa_only", ["sa"]));
    }
}
