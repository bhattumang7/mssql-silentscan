using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Catalog;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;
using SilentScan.Core.TypeInference;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class ViewColumnExpressionTypeOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(ViewColumnExpressionTypeOracleTests);

    private const string ViewName = "dbo.vw_Shapes";

    private const string GroupingViewName = "dbo.vw_Grouping";

    private const string RowsetViewName = "dbo.vw_Rowsets";

    private const string Script = """
        CREATE PARTITION FUNCTION pf_Shapes (INT) AS RANGE LEFT FOR VALUES (10);
        GO
        CREATE TABLE dbo.Src (
            t TINYINT NULL, s SMALLINT NULL, i INT NULL, b BIGINT NULL, d DECIMAL(9,2) NULL,
            v VARCHAR(20) NULL, nv NVARCHAR(20) NULL, dt DATETIME NULL, sdt SMALLDATETIME NULL,
            d2 DATETIME2(5) NULL, dto DATETIMEOFFSET(2) NULL);
        GO
        CREATE VIEW dbo.vw_Shapes AS
        SELECT
            -t AS NegTiny, +t AS PlusTiny, ~t AS NotTiny, -s AS NegSmall, -d AS NegDec,
            CONCAT(v, nv) AS ConcatMixed, CONCAT(v, i) AS ConcatInt,
            LAG(d) OVER (ORDER BY i) AS LagDec, GREATEST(i, b) AS GreatestIntBig,
            TRY_CAST(v AS INT) AS TryCastInt, TRY_CONVERT(DATE, v) AS TryConvertDate,
            PARSE(v AS DECIMAL(12,3)) AS ParseDec, TRY_PARSE(v AS INT) AS TryParseInt,
            LEFT(v, 3) AS LeftV, RIGHT(nv, 4) AS RightNv, LEFT(v, 50) AS LeftLong, LEFT(v, i) AS LeftVariable, SUBSTRING(v, 2, 3) AS SubstringLiteral, LEN(v) AS LenV,
            dt AT TIME ZONE 'UTC' AS AtTzDatetime, sdt AT TIME ZONE 'UTC' AS AtTzSmall,
            d2 AT TIME ZONE 'UTC' AS AtTzDatetime2, dto AT TIME ZONE 'UTC' AS AtTzOffset,
            {fn UCASE(v)} AS OdbcUcase, {fn LENGTH(v)} AS OdbcLength, {fn CURDATE()} AS OdbcCurdate,
            {fn NOW()} AS OdbcNow, {fn DAYNAME(dt)} AS OdbcDayname, {fn CONCAT(v, v)} AS OdbcConcat,
            $PARTITION.pf_Shapes(i) AS PartitionNumber
        FROM dbo.Src;
        GO
        CREATE VIEW dbo.vw_Grouping AS
        SELECT i, GROUPING(i) AS GroupingI, GROUPING_ID(i) AS GroupingIdI, COUNT(*) AS Cnt
        FROM dbo.Src GROUP BY ROLLUP(i);
        GO
        CREATE VIEW dbo.vw_Rowsets AS
        SELECT j.[key] AS JKey, j.[value] AS JValue, j.[type] AS JType,
               w.a AS WithInt, w.b AS WithVarchar, w.c AS WithJson, w.d AS WithDatetime2,
               CAST(sp.value AS VARCHAR(20)) AS CastSplit, so.ordinal AS SplitOrdinal, g.value AS SeriesValue, cs.value AS ApplySplit, cn.value AS ApplySplitN, cm.value AS ApplySeries
        FROM OPENJSON(N'[1]') AS j
        CROSS JOIN OPENJSON(N'[1]') WITH (a INT '$.a', b VARCHAR(5) '$.b', c NVARCHAR(MAX) '$.c' AS JSON, d DATETIME2(3) '$.d') AS w
        CROSS JOIN STRING_SPLIT(CAST('a,b' AS VARCHAR(20)), ',') AS sp
        CROSS JOIN STRING_SPLIT(CAST('a,b' AS NVARCHAR(20)), ',', 1) AS so
        CROSS JOIN GENERATE_SERIES(1, 5) AS g
        CROSS JOIN dbo.Src AS sr
        CROSS APPLY STRING_SPLIT(sr.v, ',') AS cs
        CROSS APPLY STRING_SPLIT(sr.nv, ',') AS cn
        CROSS APPLY GENERATE_SERIES(sr.t, CAST(5 AS TINYINT)) AS cm;
        """;

    protected override string Ddl => Script;

    private static string Normalize(SqlType? type) =>
        type is null ? "unknown" : (type with { Collation = null }).ToString();

    private static SqlType FromEngineText(string systemTypeName)
    {
        var parser = new TSql170Parser(initialQuotedIdentifiers: true);
        var fragment = parser.Parse(new StringReader($"DECLARE @x {systemTypeName};"), out var errors);
        Assert.Empty(errors);
        var declare = (DeclareVariableStatement)((TSqlScript)fragment).Batches[0].Statements[0];
        return SqlTypeReferenceResolver.Resolve(declare.Declarations[0].DataType, columnCollation: null)!;
    }

    private async Task<List<string>> MismatchesAsync(LineageCatalog lineage, string view)
    {
        var engine = await RowsAsync($"SELECT name, system_type_name FROM sys.dm_exec_describe_first_result_set(N'SELECT * FROM {view}', NULL, 0);");
        Assert.NotEmpty(engine);
        var relation = lineage.Find(view);
        Assert.NotNull(relation);
        var mismatches = new List<string>();
        foreach (var row in engine)
        {
            var name = (string)row[0]!;
            var expected = Normalize(FromEngineText((string)row[1]!));
            var column = relation.FindColumn(name);
            var actual = column is null ? "missing" : Normalize(ColumnProvenanceAnalysis.TryGetScalarType(column.Provenance));
            if (expected != actual)
            {
                mismatches.Add($"{name}: engine {expected}, ours {actual}");
            }
        }

        return mismatches;
    }

    private static LineageCatalog OurLineage()
    {
        var result = SqlScriptParser.ParseText("shapes.sql", Script);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        return LineageResolver.Resolve(CatalogBuilder.Build([result]), [result]);
    }

    [Fact]
    public async Task EveryViewColumnExpression_IsTypedTheWayTheEngineDescribesIt()
    {
        var mismatches = await MismatchesAsync(OurLineage(), ViewName);
        Assert.True(mismatches.Count == 0, string.Join(" | ", mismatches));
    }

    [Fact]
    public async Task OpenJsonStringSplitAndSeries_ExposeTheColumnsTheEngineDescribes()
    {
        var mismatches = await MismatchesAsync(OurLineage(), RowsetViewName);
        Assert.True(mismatches.Count == 0, string.Join(" | ", mismatches));
    }

    [Fact]
    public async Task GroupingFunctions_AreTypedTheWayTheEngineDescribesThem()
    {
        var mismatches = await MismatchesAsync(OurLineage(), GroupingViewName);
        Assert.True(mismatches.Count == 0, string.Join(" | ", mismatches));
    }
}
