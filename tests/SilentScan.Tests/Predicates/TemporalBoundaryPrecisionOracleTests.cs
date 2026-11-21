using Microsoft.Data.SqlClient;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.TemporalBoundaryPrecisionRuleId)]
public sealed class TemporalBoundaryPrecisionOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(TemporalBoundaryPrecisionOracleTests);

    protected override string Ddl => """
        CREATE TABLE dbo.Events (EventId INT IDENTITY PRIMARY KEY, OccurredAt DATETIME2(7) NOT NULL);
        """;

    [Fact]
    public async Task RowAtPrecisionGapEdge_SilentlyExcludedByThreeDigitBoundary_ButIncludedBySafeRewrite()
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(DatabaseName));
        await connection.OpenAsync();

        await using (var insertCommand = new SqlCommand(
            "INSERT INTO dbo.Events (OccurredAt) VALUES ('2024-12-31 23:59:59.9999999');", connection))
        {
            await insertCommand.ExecuteNonQueryAsync();
        }

        await using (var buggyCommand = new SqlCommand(
            "SELECT COUNT(*) FROM dbo.Events WHERE OccurredAt BETWEEN '2024-01-01' AND '2024-12-31 23:59:59.997';", connection))
        {
            var buggyCount = (int)(await buggyCommand.ExecuteScalarAsync())!;
            Assert.Equal(0, buggyCount);
        }

        await using (var safeCommand = new SqlCommand(
            "SELECT COUNT(*) FROM dbo.Events WHERE OccurredAt >= '2024-01-01' AND OccurredAt < '2025-01-01';", connection))
        {
            var safeCount = (int)(await safeCommand.ExecuteScalarAsync())!;
            Assert.Equal(1, safeCount);
        }
    }

    [Theory]
    [InlineData("2024-12-31 23:59:59", 0)]
    [InlineData("2024-12-31 23:59:59.999", 0)]
    [InlineData("2024-12-31", 0)]
    public async Task EndOfPeriodUpperBound_ExcludesALastInstantRow(string upperBound, int expectedCount)
    {
        await InsertAsync("2024-12-31 23:59:59.9999999");

        Assert.Equal(expectedCount, await CountAsync(upperBound));
    }

    [Theory]
    [InlineData("2024-06-01 17:00:00")]
    [InlineData("2024-06-01 17:00")]
    public async Task ExplicitBusinessHoursUpperBound_IsAnExactPointAndIncludesARowAtThatPoint(string upperBound)
    {
        await InsertAsync("2024-06-01 17:00:00.0000000");

        Assert.Equal(1, await CountAsync(upperBound));
    }

    private async Task InsertAsync(string value)
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(DatabaseName));
        await connection.OpenAsync();
        await using var command = new SqlCommand($"DELETE dbo.Events; INSERT INTO dbo.Events (OccurredAt) VALUES ('{value}');", connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<int> CountAsync(string upperBound)
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(DatabaseName));
        await connection.OpenAsync();
        await using var command = new SqlCommand($"SELECT COUNT(*) FROM dbo.Events WHERE OccurredAt BETWEEN '2024-01-01' AND '{upperBound}';", connection);
        return (int)(await command.ExecuteScalarAsync())!;
    }
}
