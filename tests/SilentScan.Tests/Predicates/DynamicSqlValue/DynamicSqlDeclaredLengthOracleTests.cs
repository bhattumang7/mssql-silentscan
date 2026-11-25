using Microsoft.Data.SqlClient;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates.DynamicSqlValue;

[Trait("Category", "Oracle")]
public sealed class DynamicSqlDeclaredLengthOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(DynamicSqlDeclaredLengthOracleTests);

    protected override string Ddl => string.Empty;

    [Theory]
    [InlineData("DECLARE @x VARCHAR(10) = 'abcdefghij' + 'klmnop';")]
    [InlineData("DECLARE @x NVARCHAR(4) = N'abcdef';")]
    [InlineData("DECLARE @x CHAR(3) = 'abcdef';")]
    [InlineData("DECLARE @x VARCHAR(10) = 'abc'; SET @x = @x + 'defghijklm';")]
    [InlineData("DECLARE @x VARCHAR(10) = 'abc'; SET @x += 'defghijklm';")]
    [InlineData("DECLARE @x VARCHAR(20) = 'abcdefghij' + 'klmnop';")]
    public async Task FoldedTextOfAnAssignedVariable_MatchesWhatTheEngineStores(string batch)
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(DatabaseName));
        await connection.OpenAsync();
        await using var command = new SqlCommand(batch + " SELECT RTRIM(@x);", connection);

        var stored = (string)(await command.ExecuteScalarAsync())!;

        Assert.Equal(stored, DynamicSqlTransferTests.FlattenLitText(DynamicSqlTransferTests.Run(batch)["@x"]));
    }
}
