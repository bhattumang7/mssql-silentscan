using SilentScan.Core.Catalog;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class ModuleSchemaNameBindingOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(ModuleSchemaNameBindingOracleTests);

    protected override string Ddl => """
        CREATE SCHEMA sa;
        GO
        CREATE TABLE dbo.both_t (src VARCHAR(10) NULL);
        GO
        CREATE TABLE sa.both_t (src VARCHAR(10) NULL);
        GO
        CREATE TABLE dbo.dbo_only (src VARCHAR(10) NULL);
        GO
        CREATE VIEW sa.v_both AS SELECT src FROM both_t;
        GO
        CREATE VIEW sa.v_dbo_only AS SELECT src FROM dbo_only;
        GO
        CREATE VIEW dbo.v_both AS SELECT src FROM both_t;
        GO
        CREATE VIEW sa.v_cte AS WITH both_t AS (SELECT 'cte' AS src) SELECT src FROM both_t;
        GO
        """;

    private const string Rows = """
        INSERT dbo.both_t VALUES ('dbo'); INSERT sa.both_t VALUES ('sa'); INSERT dbo.dbo_only VALUES ('dbo');
        """;

    public static TheoryData<string, string> Views() => new()
    {
        { "sa.v_both", "sa.both_t" },
        { "sa.v_dbo_only", "dbo.dbo_only" },
        { "dbo.v_both", "dbo.both_t" },
    };

    [Theory]
    [MemberData(nameof(Views))]
    public async Task UnqualifiedTableInAModule_BindsToTheModuleSchemaFirstThenDbo(string view, string expectedSource)
    {
        await ExecuteAsync(Rows);
        var engine = await ScalarAsync<string>($"SELECT TOP 1 src FROM {view};");
        Assert.Equal(expectedSource.Split('.')[0], engine);

        var result = SqlScriptParser.ParseText("schema.sql", Ddl);
        Assert.False(result.HasErrors);
        var catalog = CatalogBuilder.Build([result]);
        ModuleSchemaNameBinding.Bind([result], catalog);
        var lineage = LineageResolver.Resolve(catalog, [result]);

        var column = Assert.Single(lineage.Find(view)!.Columns);
        var source = Assert.IsType<ColumnProvenance.BaseColumn>(column.Provenance);
        Assert.Equal(expectedSource, source.TableQualifiedName, ignoreCase: true);
    }

    [Fact]
    public async Task CteNamedLikeAModuleSchemaTable_StillBindsToTheCte()
    {
        await ExecuteAsync(Rows);
        Assert.Equal("cte", await ScalarAsync<string>("SELECT TOP 1 src FROM sa.v_cte;"));

        var result = SqlScriptParser.ParseText("schema.sql", Ddl);
        var catalog = CatalogBuilder.Build([result]);
        ModuleSchemaNameBinding.Bind([result], catalog);
        var lineage = LineageResolver.Resolve(catalog, [result]);

        var column = Assert.Single(lineage.Find("sa.v_cte")!.Columns);
        Assert.IsNotType<ColumnProvenance.BaseColumn>(column.Provenance);
    }
}
