using Microsoft.Data.SqlClient;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", "silentscan/write-loss/unicode-to-non-unicode")]
[Trait("Rule", "silentscan/write-loss/numeric-scale-narrowing")]
public sealed class WriteLossOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(WriteLossOracleTests);

    protected override string Ddl => """
        CREATE TABLE dbo.T (
            Id INT IDENTITY PRIMARY KEY,
            DecCol DECIMAL(10,2) NULL,
            IntCol INT NULL,
            DateCol DATE NULL,
            VarCol VARCHAR(20) NULL,
            VarColLatin1 VARCHAR(20) COLLATE Latin1_General_100_CI_AS NULL,
            RealCol REAL NULL
        );
        """;

    [Theory]
    [InlineData("INSERT INTO dbo.T (DecCol) VALUES (123.456)", "SELECT CAST(DecCol AS VARCHAR(20)) FROM dbo.T", "123.46")]
    [InlineData("INSERT INTO dbo.T (IntCol) VALUES (7.9)", "SELECT CAST(IntCol AS VARCHAR(20)) FROM dbo.T", "7")]
    [InlineData("INSERT INTO dbo.T (DateCol) VALUES ('2024-01-15 13:45:00')", "SELECT CONVERT(VARCHAR(10), DateCol, 120) FROM dbo.T", "2024-01-15")]
    public async Task Insert_LossyAssignment_SilentlyRoundsOrTruncates_NoError(string insertSql, string selectSql, string expected)
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(DatabaseName));
        await connection.OpenAsync();

        await using (var insertCommand = new SqlCommand(insertSql, connection))
        {
            await insertCommand.ExecuteNonQueryAsync();
        }

        await using var selectCommand = new SqlCommand(selectSql, connection);
        var result = await selectCommand.ExecuteScalarAsync();
        Assert.Equal(expected, result?.ToString());
    }

    [Fact]
    public async Task Insert_UnicodeCharacterOutsideCodepage_SilentlyReplacedWithQuestionMark_NoError()
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(DatabaseName));
        await connection.OpenAsync();

        await using (var insertCommand = new SqlCommand("INSERT INTO dbo.T (VarCol) VALUES (N'日本語')", connection))
        {
            await insertCommand.ExecuteNonQueryAsync();
        }

        await using var selectCommand = new SqlCommand("SELECT VarCol, DATALENGTH(VarCol) FROM dbo.T", connection);
        await using var reader = await selectCommand.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("???", reader.GetString(0));
        Assert.Equal(3, reader.GetInt32(1));
    }

    [Fact]
    public async Task Insert_UnicodeCharacterWithinTargetCodepage_RoundTripsExactly_NoReplacement()
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(DatabaseName));
        await connection.OpenAsync();

        await using (var insertCommand = new SqlCommand("INSERT INTO dbo.T (VarColLatin1) VALUES (N'café')", connection))
        {
            await insertCommand.ExecuteNonQueryAsync();
        }

        await using var selectCommand = new SqlCommand("SELECT VarColLatin1, DATALENGTH(VarColLatin1) FROM dbo.T", connection);
        await using var reader = await selectCommand.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("café", reader.GetString(0));
        Assert.Equal(4, reader.GetInt32(1));
    }

    [Fact]
    public async Task Insert_CaseExpressionMergingDecimalBranchesOfDifferingScale_SilentlyRoundsToTargetScale_NoError()
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(DatabaseName));
        await connection.OpenAsync();

        await using (var insertCommand = new SqlCommand(
            "INSERT INTO dbo.T (DecCol) SELECT CASE WHEN 1 = 0 THEN CAST(1.10 AS DECIMAL(9,2)) ELSE CAST(1.2367 AS DECIMAL(9,4)) END",
            connection))
        {
            await insertCommand.ExecuteNonQueryAsync();
        }

        await using var selectCommand = new SqlCommand("SELECT CAST(DecCol AS VARCHAR(20)) FROM dbo.T", connection);
        var result = await selectCommand.ExecuteScalarAsync();
        Assert.Equal("1.24", result?.ToString());
    }

    [Fact]
    public async Task Assign_AvgOverRealColumn_SilentlyRoundsToSinglePrecision_NoError()
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(DatabaseName));
        await connection.OpenAsync();

        await using (var insertCommand = new SqlCommand("INSERT INTO dbo.T (RealCol) VALUES (0), (0), (1)", connection))
        {
            await insertCommand.ExecuteNonQueryAsync();
        }

        await using var selectCommand = new SqlCommand(
            """
            DECLARE @r REAL = (SELECT AVG(RealCol) FROM dbo.T);
            SELECT STR((SELECT AVG(RealCol) FROM dbo.T), 30, 16), STR(@r, 30, 16);
            """,
            connection);
        await using var reader = await selectCommand.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var doublePrecisionAverage = reader.GetString(0).Trim();
        var singlePrecisionAssigned = reader.GetString(1).Trim();
        Assert.NotEqual(doublePrecisionAverage, singlePrecisionAssigned);
    }

    [Fact]
    public async Task Insert_TooLongString_RaisesHardError_NotSilent()
    {

        await using var connection = new SqlConnection(Options.BuildConnectionString(DatabaseName));
        await connection.OpenAsync();

        await using var insertCommand = new SqlCommand("INSERT INTO dbo.T (VarCol) VALUES ('123456789012345678901')", connection);
        var exception = await Assert.ThrowsAsync<SqlException>(() => insertCommand.ExecuteNonQueryAsync());
        Assert.Contains("truncated", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
