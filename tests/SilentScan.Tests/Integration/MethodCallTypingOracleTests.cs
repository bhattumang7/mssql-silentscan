using Microsoft.Data.SqlClient;
using SilentScan.Core.Catalog;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;
using SilentScan.Tests.Support;
using SilentScan.Verify.Catalog;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class MethodCallTypingOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(MethodCallTypingOracleTests);

    protected override string Ddl => MethodCallTypingDdl.Script;

    private async Task<Dictionary<string, string?>> EngineViewColumnTypesAsync(string view)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = new SqlCommand("""
            SELECT c.name, TYPE_NAME(c.user_type_id), c.max_length, c.precision, c.scale
            FROM sys.columns c WHERE c.object_id = OBJECT_ID(@view);
            """, connection);
        command.Parameters.AddWithValue("@view", view);
        var types = new Dictionary<string, string?>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var type = LiveTypeMapper.BuildType(reader.GetString(1), reader.GetInt16(2), reader.GetByte(3), reader.GetByte(4), collationName: null);
            types[reader.GetString(0)] = type?.ToString();
        }

        return types;
    }

    [Theory]
    [InlineData("dbo.vw_Methods")]
    [InlineData("dbo.vw_UnqualifiedNodes")]
    public async Task EveryMethodCallColumn_StaticTypeMatchesTheEngineBoundViewColumn(string view)
    {
        var engine = await EngineViewColumnTypesAsync(view);
        var parse = SqlScriptParser.ParseText("methods.sql", MethodCallTypingDdl.Script);
        var lineage = LineageResolver.Resolve(CatalogBuilder.Build([parse]), [parse]);
        var resolved = lineage.Find(view)!;

        Assert.NotEmpty(engine);
        foreach (var (column, engineType) in engine)
        {
            var staticType = ColumnProvenanceAnalysis.TryGetScalarType(resolved.FindColumn(column)!.Provenance);
            Assert.Equal((column, engineType), (column, (staticType is null ? null : staticType with { Collation = null })?.ToString()));
        }
    }
}
