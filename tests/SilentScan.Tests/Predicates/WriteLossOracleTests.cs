using Microsoft.Data.SqlClient;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.WriteLossUnicodeReplacementRuleId)]
[Trait("Rule", FindingRuleIds.WriteLossNumericScaleNarrowingRuleId)]
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
            NVarCol NVARCHAR(20) NULL,
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
    [Trait("Rule", FindingRuleIds.WriteLossUnicodeReplacementRuleId)]
    public async Task Insert_QuotenameOverVarcharColumn_RoundTripsExactly_WhileQuotenameOverNvarcharColumnIsReplaced()
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(DatabaseName));
        await connection.OpenAsync();

        await using (var seed = new SqlCommand(
            "INSERT INTO dbo.T (VarColLatin1, NVarCol) VALUES ('café', N'日本語');", connection))
        {
            await seed.ExecuteNonQueryAsync();
        }

        await using (var copy = new SqlCommand(
            "UPDATE dbo.T SET VarColLatin1 = QUOTENAME(VarColLatin1);", connection))
        {
            await copy.ExecuteNonQueryAsync();
        }

        await using (var lossy = new SqlCommand("UPDATE dbo.T SET VarCol = QUOTENAME(NVarCol);", connection))
        {
            await lossy.ExecuteNonQueryAsync();
        }

        await using var select = new SqlCommand("SELECT VarColLatin1, VarCol FROM dbo.T", connection);
        await using var reader = await select.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("[café]", reader.GetString(0));
        Assert.Equal("[???]", reader.GetString(1));
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
    public async Task Assign_CaseExpressionMergingCrossCategoryStringBranches_SilentlyTruncatesToMergedLength_NoError()
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(DatabaseName));
        await connection.OpenAsync();

        await using var selectCommand = new SqlCommand(
            """
            DECLARE @v VARCHAR(5) = (SELECT CASE WHEN 1 = 0 THEN CAST('short' AS VARCHAR(3)) ELSE CAST('AAAAAAAAAAAAAAAAAAAA' AS CHAR(20)) END);
            SELECT @v;
            """,
            connection);
        var result = await selectCommand.ExecuteScalarAsync();
        Assert.Equal("AAAAA", result?.ToString());
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

    [Fact]
    public async Task NvarcharCastFromNumericBitOrGuid_RoundTripsThroughVarcharExactly_WhileNvarcharTextDoesNot()
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(DatabaseName));
        await connection.OpenAsync();

        await using var command = new SqlCommand(
            """
            SELECT
                (SELECT COUNT(*) FROM (VALUES (CAST(-12345 AS BIGINT)), (9223372036854775807), (0)) AS t(v)
                    WHERE CAST(CAST(v AS NVARCHAR(40)) AS VARCHAR(40)) <> CAST(v AS NVARCHAR(40))),
                (SELECT COUNT(*) FROM (VALUES (CAST(1.5E300 AS FLOAT)), (-0.000123), (3.14159)) AS t(v)
                    WHERE CAST(CAST(v AS NVARCHAR(40)) AS VARCHAR(40)) <> CAST(v AS NVARCHAR(40))),
                (SELECT COUNT(*) FROM (VALUES (CAST(-1234.5678 AS DECIMAL(18,4))), (CAST(0.01 AS DECIMAL(18,4)))) AS t(v)
                    WHERE CAST(CAST(v AS NVARCHAR(40)) AS VARCHAR(40)) <> CAST(v AS NVARCHAR(40))),
                (SELECT COUNT(*) FROM (VALUES (CAST(1 AS BIT)), (CAST(0 AS BIT))) AS t(v)
                    WHERE CAST(CAST(v AS NVARCHAR(40)) AS VARCHAR(40)) <> CAST(v AS NVARCHAR(40))),
                (SELECT COUNT(*) FROM (VALUES (CAST('6F9619FF-8B86-D011-B42D-00C04FC964FF' AS UNIQUEIDENTIFIER)), (CAST('00000000-0000-0000-0000-000000000000' AS UNIQUEIDENTIFIER))) AS t(v)
                    WHERE CAST(CAST(v AS NVARCHAR(40)) AS VARCHAR(40)) <> CAST(v AS NVARCHAR(40))),
                (SELECT COUNT(*) FROM (VALUES (CAST(N'日本語' AS NVARCHAR(40)))) AS t(v)
                    WHERE CAST(CAST(v AS VARCHAR(40)) AS NVARCHAR(40)) <> v);
            """, connection);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());

        Assert.Equal(0, reader.GetInt32(0));
        Assert.Equal(0, reader.GetInt32(1));
        Assert.Equal(0, reader.GetInt32(2));
        Assert.Equal(0, reader.GetInt32(3));
        Assert.Equal(0, reader.GetInt32(4));
        Assert.Equal(1, reader.GetInt32(5));
    }

    [Fact]
    public async Task NullHandlingAndNumericCastsAroundVarcharPieces_RoundTripThroughNvarcharKeepsEveryCharacter()
    {
        var kept = await ScalarAsync<string>(
            "DECLARE @c VARCHAR(20) = 'café'; DECLARE @i INT = 12; "
            + "DECLARE @m VARCHAR(200) = ISNULL(QUOTENAME(@c, ''''), 'NULL') + ', ' + ISNULL(CAST(@i AS VARCHAR(255)), 'NULL'); "
            + "SELECT @m;");

        var lost = await ScalarAsync<string>(
            "DECLARE @n NVARCHAR(20) = N'日本語'; DECLARE @m VARCHAR(200) = ISNULL(QUOTENAME(@n, ''''), 'NULL'); SELECT @m;");

        Assert.Equal("'café', 12", kept);
        Assert.Equal("'???'", lost);
    }
}
