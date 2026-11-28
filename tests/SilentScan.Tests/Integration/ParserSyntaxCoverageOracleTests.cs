using Microsoft.Data.SqlClient;
using SilentScan.Core.Parsing;
using SilentScan.Verify;
using SilentScan.Verify.Deployment;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class ParserSyntaxCoverageOracleTests
{
    private const int NewestCompatibilityLevel = 170;

    private static readonly (string Name, string Sql)[] Constructs =
    [
        ("concat-operator", "SELECT 'a' || 'b'"),
        ("concat-assign", "DECLARE @s VARCHAR(10) = 'a'; SET @s ||= 'b'"),
        ("json-column", "CREATE TABLE dbo.sx1 (j JSON)"),
        ("vector-column", "CREATE TABLE dbo.sx2 (v VECTOR(3))"),
        ("regexp-like-where", "SELECT 1 WHERE REGEXP_LIKE('abc', 'a')"),
        ("regexp-matches-tvf", "SELECT * FROM REGEXP_MATCHES('abc', 'b')"),
        ("regexp-split-tvf", "SELECT * FROM REGEXP_SPLIT_TO_TABLE('a,b', ',')"),
        ("substring-two-args", "SELECT SUBSTRING('abc', 2)"),
        ("current-date", "SELECT CURRENT_DATE"),
        ("is-distinct-from", "SELECT 1 WHERE 1 IS DISTINCT FROM 2"),
        ("window-clause", "SELECT SUM(x) OVER w FROM (VALUES (1)) v(x) WINDOW w AS (ORDER BY x)"),
        ("generate-series", "SELECT value FROM GENERATE_SERIES(1, 3)"),
        ("string-split-ordinal", "SELECT ordinal FROM STRING_SPLIT('a,b', ',', 1)"),
        ("trim-leading", "SELECT TRIM(LEADING 'x' FROM 'xxa')"),
        ("trim-both", "SELECT TRIM(BOTH 'x' FROM 'xax')"),
        ("ltrim-chars", "SELECT LTRIM('xxa', 'x')"),
        ("json-object-absent", "SELECT JSON_OBJECT('a':NULL ABSENT ON NULL)"),
        ("json-array-null", "SELECT JSON_ARRAY(1, NULL NULL ON NULL)"),
        ("json-objectagg-returning", "SELECT JSON_OBJECTAGG('a':1 RETURNING JSON)"),
        ("json-arrayagg-order", "SELECT JSON_ARRAYAGG(x ORDER BY x) FROM (VALUES (1)) v(x)"),
        ("json-object-returning", "SELECT JSON_OBJECT('a':1 RETURNING JSON)"),
        ("json-value-returning", "SELECT JSON_VALUE('{\"a\":1}', '$.a' RETURNING INT)"),
        ("json-query-array-wrapper", "SELECT JSON_QUERY('{\"a\":[1]}', '$.a' WITH ARRAY WRAPPER)"),
        ("json-contains", "DECLARE @j JSON = '{\"a\":1}'; SELECT JSON_CONTAINS(@j, 1, '$.a')"),
        ("json-index", "CREATE TABLE dbo.sx4 (id INT PRIMARY KEY, j JSON); CREATE JSON INDEX jx ON dbo.sx4 (j) FOR ('$.a')"),
        ("approx-percentile", "SELECT APPROX_PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY x) FROM (VALUES (1)) v(x)"),
        ("date-bucket", "SELECT DATE_BUCKET(week, 1, SYSDATETIME())"),
        ("datetrunc", "SELECT DATETRUNC(month, SYSDATETIME())"),
        ("greatest", "SELECT GREATEST(1, 2)"),
        ("bit-functions", "SELECT LEFT_SHIFT(1, 2), BIT_COUNT(7), GET_BIT(7, 0)"),
        ("edit-distance", "SELECT EDIT_DISTANCE('a', 'b')"),
        ("unistr", "SELECT UNISTR(N'\\0041')"),
        ("base64", "SELECT BASE64_ENCODE(0x01)"),
        ("product", "SELECT PRODUCT(x) FROM (VALUES (2)) v(x)"),
        ("ai-generate-chunks", "SELECT * FROM AI_GENERATE_CHUNKS(SOURCE = 'abc def', CHUNK_TYPE = FIXED, CHUNK_SIZE = 3)"),
        ("ledger-table", "CREATE TABLE dbo.sx6 (id INT) WITH (LEDGER = ON (APPEND_ONLY = ON))"),
        ("create-or-alter", "CREATE OR ALTER PROCEDURE dbo.sxp AS SELECT 1"),
        ("resumable-constraint", "CREATE TABLE dbo.sx7 (id INT NOT NULL); ALTER TABLE dbo.sx7 ADD CONSTRAINT pk7 PRIMARY KEY (id) WITH (ONLINE = ON, RESUMABLE = ON)"),
        ("string-agg-within", "SELECT STRING_AGG(x, ',') WITHIN GROUP (ORDER BY x) FROM (VALUES ('a')) v(x)"),
        ("json-path-exists", "SELECT JSON_PATH_EXISTS('{\"a\":1}', '$.a')"),
        ("ordered-columnstore", "CREATE TABLE dbo.sx8 (id INT); CREATE CLUSTERED COLUMNSTORE INDEX cc8 ON dbo.sx8 ORDER (id)"),
        ("vector-distance", "DECLARE @v VECTOR(3) = '[1,2,3]'; SELECT VECTOR_DISTANCE('cosine', @v, @v)"),
    ];

    [Fact]
    public async Task EveryConstructTheNewestEngineParses_OurParserParsesAtTheNewestCompatibilityLevel()
    {
        var options = SqlServerOptions.LocalDockerLatest;
        var databaseName = $"{nameof(ParserSyntaxCoverageOracleTests)}_{Guid.NewGuid():N}";
        var provisioner = new DatabaseProvisioner(options);
        await provisioner.CreateFreshAsync(databaseName);
        try
        {
            var problems = await FindParserDisagreementsAsync(options.BuildConnectionString(databaseName));
            Assert.True(problems.Count == 0, string.Join("\n", problems));
        }
        finally
        {
            SqlConnection.ClearAllPools();
            await provisioner.DropIfExistsAsync(databaseName);
        }
    }

    private static async Task<List<string>> FindParserDisagreementsAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using (var preview = new SqlCommand("ALTER DATABASE CURRENT SET COMPATIBILITY_LEVEL = 170; ALTER DATABASE SCOPED CONFIGURATION SET PREVIEW_FEATURES = ON;", connection))
        {
            await preview.ExecuteNonQueryAsync();
        }

        var problems = new List<string>();
        foreach (var (name, sql) in Constructs)
        {
            await using var command = new SqlCommand("EXEC sp_executesql @s;", connection);
            command.Parameters.AddWithValue("@s", "SET PARSEONLY ON; " + sql);
            try
            {
                await command.ExecuteNonQueryAsync();
            }
            catch (SqlException ex)
            {
                problems.Add($"{name}: engine rejects it ({ex.Number})");
                continue;
            }

            var ours = SqlScriptParser.ParseText(name + ".sql", sql, initialQuotedIdentifiers: true, NewestCompatibilityLevel);
            if (ours.HasErrors)
            {
                problems.Add($"{name}: ours fails: {string.Join("; ", ours.Errors.Select(e => e.Message))}");
            }
        }

        return problems;
    }
}
