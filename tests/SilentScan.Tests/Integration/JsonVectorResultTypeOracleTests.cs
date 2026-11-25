using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Parsing;
using SilentScan.Core.TypeInference;
using SilentScan.Verify;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class JsonVectorResultTypeOracleTests
{
    private static readonly (string Alias, string Expression)[] Calls =
    [
        ("JsonModifyOfJson", "JSON_MODIFY(j, '$.a', 1)"),
        ("JsonQueryOfJson", "JSON_QUERY(j, '$.a')"),
        ("JsonValueOfJson", "JSON_VALUE(j, '$.a')"),
        ("JsonModifyOfString", "JSON_MODIFY(s, '$.a', 1)"),
        ("JsonQueryOfString", "JSON_QUERY(s, '$.a')"),
        ("JsonContains", "JSON_CONTAINS(j, 1, '$.a')"),
        ("IsJsonOfJson", "ISJSON(j)"),
        ("VectorNormalize", "VECTOR_NORMALIZE(v, 'norm2')"),
        ("VectorDistance", "VECTOR_DISTANCE('cosine', v, v)"),
        ("VectorNorm", "VECTOR_NORM(v, 'norm2')"),
        ("VectorProperty", "VECTORPROPERTY(v, 'Dimensions')"),
    ];

    private static readonly Dictionary<string, SqlType> Columns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["j"] = new SqlType(SqlTypeCategory.Json, IsMax: true),
        ["v"] = new SqlType(SqlTypeCategory.Vector, Length: 3),
        ["s"] = new SqlType(SqlTypeCategory.VarChar, Length: 20),
    };

    private static SqlType? Ours(string expressionText)
    {
        var parser = new TSql170Parser(initialQuotedIdentifiers: true);
        var fragment = parser.Parse(new StringReader("SELECT " + expressionText), out var errors);
        Assert.Empty(errors);
        var select = (SelectStatement)((TSqlScript)fragment).Batches[0].Statements[0];
        var expression = ((SelectScalarExpression)((QuerySpecification)select.QueryExpression).SelectElements[0]).Expression;
        return ExpressionTypeInferencer.Resolve(
            expression,
            e => e is ColumnReferenceExpression c ? Columns.GetValueOrDefault(c.MultiPartIdentifier.Identifiers[^1].Value) : null,
            null);
    }

    private static string Describe(SqlType type) => type.Category switch
    {
        SqlTypeCategory.Json => "json",
        SqlTypeCategory.Vector => $"vector|{type.Length}",
        SqlTypeCategory.NVarChar => type.IsMax ? "nvarchar|max" : $"nvarchar|{type.Length}",
        _ => type.Category.ToString().ToLowerInvariant(),
    };

    private const int VectorHeaderBytes = 8;

    private const int VectorElementBytes = 4;

    private static string DescribeEngine(string typeName, short maxLength) => typeName switch
    {
        "json" => "json",
        "vector" => $"vector|{(maxLength - VectorHeaderBytes) / VectorElementBytes}",
        "nvarchar" => maxLength == -1 ? "nvarchar|max" : $"nvarchar|{maxLength / 2}",
        "sql_variant" => "sqlvariant",
        _ => typeName,
    };

    [Fact]
    public async Task JsonAndVectorFunctionResults_MatchTheStoredTypeTheEngineGivesThem()
    {
        await using var connection = new SqlConnection(SqlServerOptions.LocalDockerLatest.BuildConnectionString("tempdb"));
        await connection.OpenAsync();
        var projection = string.Join(", ", Calls.Select(c => $"{c.Expression} AS {c.Alias}"));
        await using (var create = new SqlCommand(
            $"CREATE TABLE #src (j JSON NULL, v VECTOR(3) NULL, s VARCHAR(20) NULL); SELECT {projection} INTO #out FROM #src;", connection))
        {
            await create.ExecuteNonQueryAsync();
        }

        var engine = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using (var read = new SqlCommand(
            "SELECT c.name, t.name, c.max_length FROM tempdb.sys.columns c JOIN tempdb.sys.types t ON t.user_type_id = c.user_type_id WHERE c.object_id = OBJECT_ID('tempdb..#out');", connection))
        await using (var reader = await read.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                engine[reader.GetString(0)] = DescribeEngine(reader.GetString(1), reader.GetInt16(2));
            }
        }

        var mismatches = Calls
            .Select(c => (c.Alias, c.Expression, Ours: Ours(c.Expression) is { } t ? Describe(t) : "unresolved", Engine: engine[c.Alias]))
            .Where(r => r.Ours != r.Engine)
            .Select(r => $"{r.Expression}: ours {r.Ours} vs engine {r.Engine}")
            .ToList();

        Assert.True(mismatches.Count == 0, string.Join("\n", mismatches));
    }
}
