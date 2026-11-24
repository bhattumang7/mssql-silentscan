using Microsoft.Data.SqlClient;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;
using SilentScan.Tests.Support;
using SilentScan.Verify.Catalog;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class FullTextTableReferenceOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(FullTextTableReferenceOracleTests);

    private const string ViewDdl = """
        CREATE VIEW dbo.vw_Ranked AS
        SELECT k.[KEY] AS DocKey, k.[RANK] AS DocRank, f.[KEY] AS FreeKey, f.[RANK] AS FreeRank
        FROM CONTAINSTABLE(dbo.Doc, Txt, 'x') AS k
        JOIN FREETEXTTABLE(dbo.Doc, *, 'x', 5) AS f ON f.[KEY] = k.[KEY];
        """;

    protected override string Ddl => $"""
        CREATE TABLE dbo.Doc (Code VARCHAR(12) NOT NULL CONSTRAINT UQ_Doc UNIQUE, Id INT NOT NULL CONSTRAINT PK_Doc PRIMARY KEY, Txt NVARCHAR(200) NULL);
        GO
        CREATE FULLTEXT CATALOG ftc AS DEFAULT;
        GO
        CREATE FULLTEXT INDEX ON dbo.Doc (Txt) KEY INDEX UQ_Doc;
        GO
        {ViewDdl}
        """;

    [Fact]
    public async Task KeyAndRankColumns_StaticTypesMatchTheEngineBoundViewColumns()
    {
        var catalog = await new LiveCatalogReader(Options.BuildConnectionString(DatabaseName)).ReadAsync();
        var parse = SqlScriptParser.ParseText("view.sql", ViewDdl);
        var view = LineageResolver.Resolve(catalog, [parse]).Find("dbo.vw_Ranked")!;

        await using var connection = await OpenConnectionAsync();
        await using var command = new SqlCommand("""
            SELECT c.name, TYPE_NAME(c.user_type_id), c.max_length, c.precision, c.scale
            FROM sys.columns c WHERE c.object_id = OBJECT_ID(N'dbo.vw_Ranked') ORDER BY c.column_id;
            """, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var compared = 0;
        while (await reader.ReadAsync())
        {
            var name = reader.GetString(0);
            var engine = LiveTypeMapper.BuildType(reader.GetString(1), reader.GetInt16(2), reader.GetByte(3), reader.GetByte(4), collationName: null);
            var ours = ColumnProvenanceAnalysis.TryGetScalarType(view.FindColumn(name)!.Provenance);
            Assert.Equal((name, engine?.ToString()), (name, (ours is null ? null : ours with { Collation = null })?.ToString()));
            compared++;
        }

        Assert.Equal(4, compared);
        Assert.Equal("Code", catalog.Find("dbo.Doc")!.FullTextKeyColumnName);
    }
}
