using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.Parsing;
using SilentScan.Core.TypeInference;
using SilentScan.Tests.Support;
using SilentScan.Verify;
using SilentScan.Verify.Catalog;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class ArgumentShapedFunctionOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(ArgumentShapedFunctionOracleTests);

    protected override SqlServerOptions Options => SqlServerOptions.LocalDockerLatest;

    protected override string Ddl => "SELECT 1 AS Placeholder INTO dbo.Placeholder;";

    private static readonly string[] ArgumentDeclarations =
    [
        "tinyint", "smallint", "int", "bigint", "decimal(5,0)", "decimal(10,3)", "decimal(18,2)", "decimal(38,10)",
        "money", "smallmoney", "float", "real", "varchar(10)", "nvarchar(10)", "bit",
    ];

    private static readonly string[] NumericShapedFunctions = ["ABS", "SIGN", "ROUND", "CEILING", "FLOOR", "POWER", "RADIANS", "DEGREES"];

    private static readonly string[] DateTimeDeclarations = ["datetime", "smalldatetime", "date", "datetime2(0)", "datetime2(3)", "datetime2(7)", "varchar(30)", "nvarchar(30)"];

    private static async Task<string?> EngineTypeAsync(SqlConnection connection, string declaration, string expression)
    {
        await using var command = new SqlCommand(
            "SELECT system_type_name, max_length, precision, scale FROM sys.dm_exec_describe_first_result_set(@sql, NULL, 0);", connection);
        command.Parameters.AddWithValue("@sql", $"DECLARE @x {declaration}; SELECT {expression} AS X");
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), expression);
        var typeName = reader.GetString(0).Split('(')[0];
        return LiveTypeMapper.BuildType(typeName, reader.GetInt16(1), reader.GetByte(2), reader.GetByte(3), collationName: null)?.ToString();
    }

    private static SqlType? Declared(string declaration) =>
        SqlTypeReferenceResolver.Resolve(new TSql170Parser(initialQuotedIdentifiers: true).ParseScalarDataType(new StringReader(declaration), out _), columnCollation: null);

    [Fact]
    public async Task NumericShapedFunctions_ReturnTheTypeTheEngineReportsForEveryNumericArgumentType()
    {
        await using var connection = await OpenConnectionAsync();
        var mismatches = new List<string>();
        foreach (var function in NumericShapedFunctions)
        {
            Assert.True(ArgumentShapedFunctionTypes.IsArgumentShaped(function), function);
            var extra = function is "ROUND" or "POWER" ? ", 2" : string.Empty;
            foreach (var declaration in ArgumentDeclarations)
            {
                var engine = await EngineTypeAsync(connection, declaration, $"{function}(@x{extra})");
                var ours = ArgumentShapedFunctionTypes.Resolve(function, Declared(declaration))?.ToString();
                if (engine != ours)
                {
                    mismatches.Add($"{function}({declaration}): engine {engine}, ours {ours}");
                }
            }
        }

        Assert.Empty(mismatches);
    }

    [Fact]
    public async Task ToDateTimeOffset_ReturnsTheScaleTheEngineReportsForEveryTemporalOrStringArgumentType()
    {
        await using var connection = await OpenConnectionAsync();
        var mismatches = new List<string>();
        foreach (var declaration in DateTimeDeclarations)
        {
            var engine = await EngineTypeAsync(connection, declaration, "TODATETIMEOFFSET(@x, 60)");
            var ours = ArgumentShapedFunctionTypes.Resolve("TODATETIMEOFFSET", Declared(declaration))?.ToString();
            if (engine != ours)
            {
                mismatches.Add($"TODATETIMEOFFSET({declaration}): engine {engine}, ours {ours}");
            }
        }

        Assert.Empty(mismatches);
    }
}
