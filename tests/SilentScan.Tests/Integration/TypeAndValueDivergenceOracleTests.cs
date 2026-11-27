using SilentScan.Core.Catalog;
using SilentScan.Core.Diagnostics;
using SilentScan.Core.Lineage;
using SilentScan.Core.Predicates;
using SilentScan.Core.Predicates.DynamicSqlValue;
using SilentScan.Core.TypeInference;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class TypeAndValueDivergenceOracleTests : EngineDivergenceOracleTestBase
{
    protected override string DatabaseNameSeed => nameof(TypeAndValueDivergenceOracleTests);

    private async Task<string> EngineTypeAsync(string expression) =>
        (await ScalarAsync<string>(
            $"SELECT system_type_name FROM sys.dm_exec_describe_first_result_set(N'SELECT {expression.Replace("'", "''", StringComparison.Ordinal)} AS c', NULL, 0);"))!;

    private static SqlType? OurType(string expression)
    {
        var (result, _) = Parse($"SELECT {expression};");
        var select = (SelectStatement)((TSqlScript)result.Fragment).Batches[0].Statements[0];
        var element = (SelectScalarExpression)((QuerySpecification)select.QueryExpression).SelectElements[0];
        return ExpressionTypeInferencer.Resolve(element.Expression, _ => null, null);
    }

    [Theory]
    [InlineData("LEN(CAST('a' AS VARCHAR(MAX)))", "bigint", SqlTypeCategory.BigInt)]
    [InlineData("DATALENGTH(CAST(0x01 AS VARBINARY(MAX)))", "bigint", SqlTypeCategory.BigInt)]
    [InlineData("LEN(CAST('a' AS VARCHAR(10)))", "int", SqlTypeCategory.Int)]
    [InlineData("DATALENGTH(CAST('a' AS NVARCHAR(10)))", "int", SqlTypeCategory.Int)]
    public async Task LengthFunctions_ReturnBigintOnlyForLargeValueArguments(string expression, string engineType, SqlTypeCategory category)
    {
        Assert.Equal(engineType, await EngineTypeAsync(expression));
        Assert.Equal(category, OurType(expression)!.Category);
    }

    [Theory]
    [InlineData("SUM", SqlTypeCategory.Money, "money")]
    [InlineData("AVG", SqlTypeCategory.Money, "money")]
    [InlineData("MAX", SqlTypeCategory.SmallMoney, "smallmoney")]
    public async Task SumAndAverageOfSmallMoney_WidenToMoney(string aggregate, SqlTypeCategory category, string engineType)
    {
        Assert.Equal(
            engineType,
            await ScalarAsync<string>(
                $"SELECT system_type_name FROM sys.dm_exec_describe_first_result_set(N'SELECT {aggregate}(x) AS c FROM (VALUES (CAST(1 AS SMALLMONEY))) t(x)', NULL, 0);"));
        Assert.Equal(
            category,
            BuiltinFunctionTypeResolver.AdjustArgumentTypeFunctionResult(aggregate, new SqlType(SqlTypeCategory.SmallMoney))!.Category);
    }

    [Theory]
    [InlineData("0.05")]
    [InlineData("0.5")]
    [InlineData("0.0")]
    [InlineData("00.5")]
    [InlineData("0100.5")]
    [InlineData("1.05")]
    public async Task DecimalLiteralPrecision_DoesNotCountLeadingZerosOfTheIntegerPart(string literal)
    {
        var ours = OurType(literal)!;
        Assert.Equal(await EngineTypeAsync(literal), $"numeric({ours.Precision},{ours.Scale})");
    }

    [Theory]
    [InlineData("2147483647")]
    [InlineData("2147483648")]
    [InlineData("0002147483647")]
    [InlineData("0002147483648")]
    [InlineData("00000000001")]
    [InlineData("000000000001")]
    [InlineData("02147483647")]
    [InlineData("99999999999999999999")]
    public async Task IntegerLiteral_IsNumericOnceItLeavesTheIntRange(string literal)
    {
        var ours = OurType(literal)!;
        Assert.Equal(
            await EngineTypeAsync(literal),
            ours.Category == SqlTypeCategory.Int ? "int" : $"numeric({ours.Precision},{ours.Scale})");
    }

    [Theory]
    [InlineData("0x")]
    [InlineData("0x01")]
    [InlineData("0x010203")]
    [InlineData("0x1")]
    public async Task BinaryLiteral_IsVarBinaryOfAtLeastOneByte(string literal)
    {
        var ours = OurType(literal)!;
        Assert.Equal(SqlTypeCategory.VarBinary, ours.Category);
        Assert.Equal(await EngineTypeAsync(literal), $"varbinary({ours.Length})");
    }

    [Theory]
    [InlineData("DATETIME2(3)", "'2024-01-01 10:00:00.001'", true)]
    [InlineData("DATETIME2(7)", "'2024-01-01 10:00:00.9999999'", true)]
    [InlineData("TIME(4)", "'10:00:00.0010'", true)]
    [InlineData("DATETIME2(2)", "'2024-01-01 10:00:00.01'", false)]
    [Trait("Rule", FindingRuleIds.WriteLossTemporalScaleNarrowingRuleId)]
    public async Task AssigningIntoDateTime_RoundsOnlySourcesFinerThanTheGrid(string sourceType, string literal, bool lossy)
    {
        Assert.Equal(lossy, await ScalarAsync<int>($"""
            DECLARE @s {sourceType} = {literal};
            DECLARE @t DATETIME = @s;
            SELECT CASE WHEN CAST(@t AS {sourceType}) <> @s THEN 1 ELSE 0 END;
            """) == 1);

        var (result, catalog) = Parse($"DECLARE @s {sourceType} = {literal}; DECLARE @t DATETIME; SET @t = @s;");
        var findings = TypedPredicateExtractor.Extract(result, catalog, LineageResolver.Resolve(catalog, [result])).WriteLossFindings;
        Assert.Equal(lossy, findings.Any(f => f.Kind == WriteLossKind.TemporalScaleNarrowing));
    }

    [Theory]
    [InlineData("SQL_Latin1_General_CP1_CI_AS", 8000, true)]
    [InlineData("Latin1_General_CS_AS", 3000, false)]
    [Trait("Rule", FindingRuleIds.BoundedStringBuiltinTruncationReplaceResultTruncatedRuleId)]
    public async Task Replace_MatchesUnderTheCollation_SoCaseInsensitiveMatchesCanTruncate(string collation, int engineLength, bool truncated)
    {
        var literal = new string('A', 3000);
        Assert.Equal(engineLength, await ScalarAsync<int>($"SELECT LEN(REPLACE('{literal}' COLLATE {collation}, 'a', 'bbb'));"));

        var (result, catalog) = Parse($"SELECT REPLACE('{literal}', 'a', 'bbb');");
        catalog.DefaultCollation = new Collation(collation);
        Assert.Equal(truncated, BoundedStringBuiltinTruncationScanner.Scan(result, catalog).Count > 0);
    }

    [Theory]
    [InlineData("SUBSTRING(@s, 1, LEN(@s) - 1)", "abc  ")]
    [InlineData("SUBSTRING(@s, 2, LEN(@s))", "abc  ")]
    [InlineData("SUBSTRING(@s, 0, LEN(@s))", "abc  ")]
    [InlineData("SUBSTRING(@s, 2, LEN(@s))", "  ab ")]
    [InlineData("SUBSTRING(@s, 1, LEN(@s) - 1)", "abc")]
    public async Task DynamicSqlSubstringByLen_FoldsTheSameTextTheEngineProduces(string expression, string value)
    {
        var engine = await ScalarAsync<string>($"DECLARE @s VARCHAR(20) = '{value}'; SELECT {expression};");

        var (result, _) = Parse($"SELECT {expression};");
        var select = (SelectStatement)((TSqlScript)result.Fragment).Batches[0].Statements[0];
        var element = (SelectScalarExpression)((QuerySpecification)select.QueryExpression).SelectElements[0];
        var span = new SourceSpan("test.sql", 1, 1);
        var state = new Dictionary<string, SqlTextValue>(StringComparer.OrdinalIgnoreCase)
        {
            ["@s"] = new SqlTextValue.Template([new TemplatePiece.Lit(value, span, PrefixLength: 0)]),
        };
        var folded = Assert.IsType<SqlTextValue.Template>(ExpressionEvaluator.Fold(element.Expression, state, "test.sql", 32));
        Assert.Equal(engine, string.Concat(folded.Pieces.OfType<TemplatePiece.Lit>().Select(l => l.Text)));
    }

    [Theory]
    [InlineData("DECIMAL(5,2)", "DECIMAL(5,1)", true)]
    [InlineData("MONEY", "DECIMAL(9,1)", true)]
    [InlineData("SMALLMONEY", "DECIMAL(9,1)", true)]
    [InlineData("DECIMAL(5,2)", "INT", false)]
    [Trait("Rule", FindingRuleIds.ProcCallArgumentMismatchRuleId)]
    [Trait("Rule", FindingRuleIds.SpExecuteSqlParameterMismatchRuleId)]
    [Trait("Rule", FindingRuleIds.WriteLossNumericScaleNarrowingRuleId)]
    public async Task NumericRoundAbort_TurnsScaleNarrowingIntoAnError_SoNothingIsReported(string source, string target, bool raises)
    {
        var call = $"DECLARE @a {source} = 1.25; EXEC sp_executesql N'SELECT @p', N'@p {target}', @a;";
        var error = await SqlErrorNumberAsync($"SET NUMERIC_ROUNDABORT ON; {call}");
        Assert.Equal(raises, error is 8114 or 8115);
        Assert.Null(await SqlErrorNumberAsync(call));

        if (!raises)
        {
            return;
        }

        var procedure = $"CREATE PROCEDURE dbo.P @p {target} AS SELECT @p;";
        var script = $"{procedure}\nGO\nSET NUMERIC_ROUNDABORT ON;\nDECLARE @a {source} = 1.25;\nEXEC dbo.P @a;\n{call.Replace($"DECLARE @a {source} = 1.25; ", string.Empty, StringComparison.Ordinal)}\nDECLARE @t {target};\nSET @t = @a;";
        var (result, catalog) = Parse(script);
        var graph = ProcCallGraphBuilder.Build([result], catalog, new SkipLedger());
        Assert.Empty(ProcCallArgumentMismatchScanner.Scan(graph));
        Assert.Empty(SpExecuteSqlParameterMismatchScanner.Scan(graph));
        Assert.DoesNotContain(
            TypedPredicateExtractor.Extract(result, catalog, LineageResolver.Resolve(catalog, [result])).WriteLossFindings,
            f => f.Kind == WriteLossKind.NumericScaleNarrowing);

        var (quiet, quietCatalog) = Parse(script.Replace("SET NUMERIC_ROUNDABORT ON;", string.Empty, StringComparison.Ordinal));
        var quietGraph = ProcCallGraphBuilder.Build([quiet], quietCatalog, new SkipLedger());
        Assert.NotEmpty(ProcCallArgumentMismatchScanner.Scan(quietGraph));
        Assert.NotEmpty(SpExecuteSqlParameterMismatchScanner.Scan(quietGraph));
    }

    [Fact]
    public async Task DynamicSqlReplaceOverAccentedText_IsNotFolded_BecauseTheCollationCanMatchAcrossAccents()
    {
        Assert.Equal("cafX", await ScalarAsync<string>("SELECT REPLACE(N'caf' + NCHAR(233) COLLATE Latin1_General_CI_AI, N'e', N'X');"));

        BuiltinFoldResult Fold(string source) => BuiltinRegistry.Fold(new BuiltinCall(
            "REPLACE",
            [new BuiltinArgument.Text(source), new BuiltinArgument.Text("e"), new BuiltinArgument.Text("X")],
            new SourceSpan("test.sql", 1, 1)));
        Assert.IsType<BuiltinFoldResult.Fail>(Fold("caf" + (char)233));
        Assert.IsType<BuiltinFoldResult.Ok>(Fold("cafe"));
    }
}
