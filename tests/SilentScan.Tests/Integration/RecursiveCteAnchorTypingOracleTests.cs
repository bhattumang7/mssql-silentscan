using Microsoft.Data.SqlClient;
using SilentScan.Core.Catalog;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;
using SilentScan.Tests.Support;
using SilentScan.Verify.Catalog;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class RecursiveCteAnchorTypingOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(RecursiveCteAnchorTypingOracleTests);

    protected override string Ddl => "CREATE TABLE dbo.Unused (Id INT NOT NULL);";

    public static TheoryData<string> Bodies => new()
    {
        "SELECT CAST(1 AS INT) AS a, CAST('x' AS VARCHAR(3)) AS b UNION ALL SELECT CAST(2 AS BIGINT), CAST('yy' AS VARCHAR(5)) UNION ALL SELECT c.a + 1, CAST(c.b AS VARCHAR(5)) FROM c WHERE c.a < 3",
        "SELECT CAST(1 AS INT) AS a, CAST('x' AS VARCHAR(3)) AS b UNION ALL SELECT CAST(c.a + 1 AS INT), CAST(c.b AS VARCHAR(3)) FROM c WHERE c.a < 3 UNION ALL SELECT CAST(c.a + 2 AS INT), CAST(c.b AS VARCHAR(3)) FROM c WHERE c.a < 3",
        "(SELECT CAST(1 AS SMALLINT) AS a, CAST(1.5 AS DECIMAL(5,1)) AS b UNION ALL SELECT CAST(2 AS INT), CAST(2.25 AS DECIMAL(6,2))) UNION ALL SELECT c.a + 1, CAST(c.b AS DECIMAL(6,2)) FROM c WHERE c.a < 3",
    };

    [Theory]
    [MemberData(nameof(Bodies))]
    public async Task MergedAnchorTypes_AreTheEngineCteColumnTypes_AndTheEngineAcceptsTheBody(string body)
    {
        var view = $"CREATE VIEW dbo.vw_Cte AS WITH c AS ({body}) SELECT a, b FROM c;";
        await ExecuteAsync(view);

        var catalog = await new LiveCatalogReader(Options.BuildConnectionString(DatabaseName)).ReadAsync();
        var lineage = LineageResolver.Resolve(catalog, [SqlScriptParser.ParseText("view.sql", view)]);
        var resolved = lineage.Find("dbo.vw_Cte")!;

        await using var connection = await OpenConnectionAsync();
        await using var command = new SqlCommand("""
            SELECT c.name, TYPE_NAME(c.user_type_id), c.max_length, c.precision, c.scale
            FROM sys.columns c WHERE c.object_id = OBJECT_ID(N'dbo.vw_Cte') ORDER BY c.column_id;
            """, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var compared = 0;
        while (await reader.ReadAsync())
        {
            var name = reader.GetString(0);
            var engine = LiveTypeMapper.BuildType(reader.GetString(1), reader.GetInt16(2), reader.GetByte(3), reader.GetByte(4), collationName: null)?.ToString();
            var ours = ColumnProvenanceAnalysis.TryGetScalarType(resolved.FindColumn(name)!.Provenance);
            Assert.Equal((name, engine), (name, (ours is null ? null : ours with { Collation = null })?.ToString()));
            compared++;
        }

        Assert.Equal(2, compared);
    }

    [Fact]
    public async Task RecursiveMemberNarrowerThanTheMergedAnchors_IsRejectedWithMsg240()
    {
        var exception = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(
            "WITH c(a) AS (SELECT CAST(1 AS INT) UNION ALL SELECT CAST(2 AS BIGINT) UNION ALL SELECT CAST(a + 1 AS INT) FROM c WHERE a < 3) SELECT a FROM c;"));

        Assert.Equal(240, exception.Number);
    }
}
