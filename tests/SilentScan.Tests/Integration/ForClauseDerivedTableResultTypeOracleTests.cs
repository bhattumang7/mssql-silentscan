using Microsoft.Data.SqlClient;
using SilentScan.Verify;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class ForClauseDerivedTableResultTypeOracleTests
{
    private readonly SqlServerOptions _options = SqlServerOptions.LocalDocker;

    private async Task<string> DescribeSystemTypeName(string forClause)
    {
        var select =
            "SELECT sn.x FROM (SELECT CAST(a AS varchar(5)) + ';' AS [data()] FROM (VALUES ('a'), ('b')) t(a) " +
            forClause + ") sn(x)";
        await using var connection = new SqlConnection(_options.BuildConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT system_type_name FROM sys.dm_exec_describe_first_result_set(N'" +
            select.Replace("'", "''") + "', NULL, 0);";
        return (string)(await command.ExecuteScalarAsync())!;
    }

    [Theory]
    [InlineData("FOR XML PATH('')", "nvarchar(max)")]
    [InlineData("FOR XML RAW", "nvarchar(max)")]
    [InlineData("FOR JSON PATH", "nvarchar(max)")]
    [InlineData("FOR XML PATH(''), TYPE", "xml")]
    public async Task ForClauseDerivedTable_ExposesOneColumnOfTheEnginesFixedType(string forClause, string expected)
    {
        Assert.Equal(expected, await DescribeSystemTypeName(forClause));
    }

    [Fact]
    public async Task DerivedTableWithoutForClause_KeepsTheSourceColumnType()
    {
        Assert.Equal("varchar(6)", await DescribeSystemTypeName(string.Empty));
    }
}
