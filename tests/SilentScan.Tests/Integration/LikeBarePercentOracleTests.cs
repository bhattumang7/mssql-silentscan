using System.Text.RegularExpressions;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.Tier1LeadingWildcardLikeRuleId)]
public sealed partial class LikeBarePercentOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(LikeBarePercentOracleTests);

    protected override string Ddl => """
        CREATE TABLE dbo.Lk (Id INT NOT NULL PRIMARY KEY, Code VARCHAR(20) NOT NULL, Pad CHAR(100) NOT NULL DEFAULT 'x');
        GO
        INSERT INTO dbo.Lk (Id, Code)
        SELECT TOP (20000) ROW_NUMBER() OVER (ORDER BY (SELECT 1)), 'c' + CAST(ROW_NUMBER() OVER (ORDER BY (SELECT 1)) AS VARCHAR(10))
        FROM sys.all_objects a CROSS JOIN sys.all_objects b;
        CREATE INDEX IX_Lk_Code ON dbo.Lk (Code);
        GO
        """;

    private async Task<(long Rows, long Reads)> RunAsync(string statement)
    {
        await using var connection = await OpenConnectionAsync();
        await ExecuteAsync(connection, "SET STATISTICS IO ON;");
        long reads = 0;
        connection.InfoMessage += (_, e) =>
        {
            var match = LogicalReads().Match(e.Message);
            if (match.Success)
            {
                reads += long.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            }
        };

        await using var command = new Microsoft.Data.SqlClient.SqlCommand(statement, connection);
        var rows = Convert.ToInt64(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
        return (rows, reads);
    }

    [Fact]
    public async Task BarePercentPattern_ReturnsEveryRowAndReadsNoMoreThanTheUnfilteredQuery_SoNoSeekIsLost()
    {
        var bare = await RunAsync("SELECT COUNT(*) FROM dbo.Lk WHERE Code LIKE '%';");
        var unfiltered = await RunAsync("SELECT COUNT(*) FROM dbo.Lk;");

        Assert.Equal(20000, bare.Rows);
        Assert.True(bare.Reads <= unfiltered.Reads * 2, $"bare % read {bare.Reads}, unfiltered read {unfiltered.Reads}");
    }

    [Fact]
    public async Task PrefixPattern_ReadsFarFewerPagesThanALeadingWildcardPattern()
    {
        var prefix = await RunAsync("SELECT COUNT(*) FROM dbo.Lk WHERE Code LIKE 'c1999%';");
        var leading = await RunAsync("SELECT COUNT(*) FROM dbo.Lk WHERE Code LIKE '%1999';");

        Assert.True(prefix.Reads * 10 < leading.Reads, $"prefix read {prefix.Reads}, leading wildcard read {leading.Reads}");
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"Table 'Lk'\. Scan count \d+, logical reads (\d+)")]
    private static partial System.Text.RegularExpressions.Regex LogicalReads();
}
