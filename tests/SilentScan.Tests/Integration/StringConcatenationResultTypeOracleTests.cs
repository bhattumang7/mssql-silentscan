using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SilentScan.Core.TypeInference;
using SilentScan.Verify;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class StringConcatenationResultTypeOracleTests
{
    private readonly SqlServerOptions _options = SqlServerOptions.LocalDocker;

    private static readonly Dictionary<string, SqlType> Operands = new(StringComparer.OrdinalIgnoreCase)
    {
        ["varchar(11)"] = new(SqlTypeCategory.VarChar, Length: 11),
        ["char(8)"] = new(SqlTypeCategory.Char, Length: 8),
        ["char(10)"] = new(SqlTypeCategory.Char, Length: 10),
        ["varchar(10)"] = new(SqlTypeCategory.VarChar, Length: 10),
        ["nchar(5)"] = new(SqlTypeCategory.NChar, Length: 5),
        ["nvarchar(5)"] = new(SqlTypeCategory.NVarChar, Length: 5),
        ["char(8000)"] = new(SqlTypeCategory.Char, Length: 8000),
        ["varchar(5000)"] = new(SqlTypeCategory.VarChar, Length: 5000),
        ["nvarchar(3000)"] = new(SqlTypeCategory.NVarChar, Length: 3000),
        ["char(4000)"] = new(SqlTypeCategory.Char, Length: 4000),
        ["nchar(1)"] = new(SqlTypeCategory.NChar, Length: 1),
    };

    [Theory]
    [InlineData("varchar(11)", "char(8)")]
    [InlineData("char(8)", "varchar(11)")]
    [InlineData("char(8)", "char(8)")]
    [InlineData("char(10)", "nchar(5)")]
    [InlineData("varchar(10)", "nchar(5)")]
    [InlineData("char(10)", "nvarchar(5)")]
    [InlineData("nchar(5)", "nvarchar(5)")]
    [InlineData("char(8000)", "char(8000)")]
    [InlineData("varchar(5000)", "nvarchar(3000)")]
    [InlineData("char(4000)", "nchar(1)")]
    public async Task ConcatenationResultType_InferencerAgreesWithEngine(string leftType, string rightType)
    {
        await using var connection = new SqlConnection(_options.BuildConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        var batch = $"DECLARE @a {leftType} = 'x', @b {rightType} = 'y'; SELECT @a + @b AS r";
        command.CommandText =
            "SELECT system_type_name FROM sys.dm_exec_describe_first_result_set(N'" + batch.Replace("'", "''") + "', NULL, 0);";
        var engineType = (string)(await command.ExecuteScalarAsync())!;

        var parser = new TSql160Parser(true);
        using var reader = new StringReader("SELECT a + b;");
        var script = (TSqlScript)parser.Parse(reader, out _);
        var expression = ((SelectScalarExpression)((QuerySpecification)((SelectStatement)script.Batches[0].Statements[0]).QueryExpression).SelectElements[0]).Expression;

        var inferred = ExpressionTypeInferencer.Resolve(
            expression,
            e => e is ColumnReferenceExpression { MultiPartIdentifier.Identifiers: [.., { } last] }
                ? Operands[last.Value == "a" ? leftType : rightType]
                : null,
            typeAliases: null);

        Assert.NotNull(inferred);
        var category = inferred.Category switch
        {
            SqlTypeCategory.Char => "char",
            SqlTypeCategory.VarChar => "varchar",
            SqlTypeCategory.NChar => "nchar",
            _ => "nvarchar",
        };
        Assert.Equal(engineType, $"{category}({inferred.Length})");
    }
}
