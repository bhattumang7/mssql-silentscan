using System.Globalization;
using System.Xml.Linq;
using Microsoft.Data.SqlClient;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.DatabaseConfigurationAutoCreateStatisticsOffRuleId)]
[Trait("Rule", FindingRuleIds.DatabaseConfigurationAutoUpdateStatisticsOffRuleId)]
public sealed class AutoStatisticsEstimateOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(AutoStatisticsEstimateOracleTests);

    protected override string Ddl => """
        CREATE TABLE dbo.T (Id INT NOT NULL PRIMARY KEY, A INT NOT NULL, B INT NOT NULL);
        GO
        INSERT INTO dbo.T (Id, A, B)
        SELECT TOP (50000) ROW_NUMBER() OVER (ORDER BY (SELECT 1)), 1, 1 FROM sys.all_objects a CROSS JOIN sys.all_objects b;
        CREATE INDEX IX_T_A ON dbo.T (A);
        UPDATE STATISTICS dbo.T WITH FULLSCAN;
        GO
        """;

    private async Task<double> EstimatedRowsAfterGrowthAsync(bool autoStatistics, string column)
    {
        var state = autoStatistics ? "ON" : "OFF";
        await using var connection = await OpenConnectionAsync();
        await ExecuteAsync(connection, $"ALTER DATABASE [{DatabaseName}] SET AUTO_CREATE_STATISTICS {state}; ALTER DATABASE [{DatabaseName}] SET AUTO_UPDATE_STATISTICS {state};");
        await ExecuteAsync(connection, "INSERT INTO dbo.T (Id, A, B) SELECT TOP (50000) 100000 + ROW_NUMBER() OVER (ORDER BY (SELECT 1)), 2, 2 FROM sys.all_objects a CROSS JOIN sys.all_objects b;");

        await using var command = new SqlCommand($"SET STATISTICS XML ON; SELECT COUNT(*) FROM dbo.T WHERE {column} = 2 OPTION (RECOMPILE);", connection);
        await using var reader = await command.ExecuteReaderAsync();
        string? plan = null;
        do
        {
            while (await reader.ReadAsync())
            {
                if (reader.FieldCount == 1 && reader.GetValue(0) is string text && text.StartsWith("<ShowPlanXML", StringComparison.Ordinal))
                {
                    plan = text;
                }
            }
        }
        while (await reader.NextResultAsync());

        var access = XDocument.Parse(plan!).Descendants()
            .Where(e => e.Name.LocalName == "RelOp"
                && (e.Attribute("PhysicalOp")!.Value.Contains("Scan", StringComparison.Ordinal) || e.Attribute("PhysicalOp")!.Value.Contains("Seek", StringComparison.Ordinal)))
            .First();
        return double.Parse(access.Attribute("EstimateRows")!.Value, CultureInfo.InvariantCulture);
    }

    [Theory]
    [InlineData("A")]
    [InlineData("B")]
    public async Task WithBothSettingsOff_TheEstimateStaysAtTheStaleOrGuessedValue(string column)
    {
        var estimate = await EstimatedRowsAfterGrowthAsync(autoStatistics: false, column);

        Assert.True(estimate < 5000, $"estimate {estimate} for 50000 matching rows");
    }

    [Theory]
    [InlineData("A")]
    [InlineData("B")]
    public async Task WithBothSettingsOn_TheEstimateTracksTheRealRowCount(string column)
    {
        var estimate = await EstimatedRowsAfterGrowthAsync(autoStatistics: true, column);

        Assert.True(estimate > 40000, $"estimate {estimate} for 50000 matching rows");
    }
}
