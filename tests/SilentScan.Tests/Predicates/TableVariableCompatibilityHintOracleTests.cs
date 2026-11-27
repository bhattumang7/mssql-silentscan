using Microsoft.Data.SqlClient;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.QueryAntiPatternTableVariableLowCompatEstimateRuleId)]
[Trait("Rule", FindingRuleIds.QueryAntiPatternTableVariableStaleEstimateInLoopRuleId)]
public sealed class TableVariableCompatibilityHintOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(TableVariableCompatibilityHintOracleTests);

    protected override string Ddl => """
        CREATE TABLE dbo.Big (Id INT NOT NULL PRIMARY KEY);
        INSERT INTO dbo.Big SELECT TOP (5000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) FROM sys.all_objects a CROSS JOIN sys.all_objects b;
        GO
        """;

    [Theory]
    [InlineData(160, 140, 1d)]
    [InlineData(140, 160, 1000d)]
    public async Task TheStatementHintDecidesWhetherTheTableVariableIsCompiledWithItsRealRowCount(
        int databaseLevel, int hintedLevel, double expectedRows)
    {
        await ExecuteAsync($"ALTER DATABASE CURRENT SET COMPATIBILITY_LEVEL = {databaseLevel};");
        await using var connection = await OpenConnectionAsync();
        await using var command = new SqlCommand($"""
            DECLARE @t TABLE (Id INT);
            INSERT @t SELECT TOP (1000) Id FROM dbo.Big;
            SET STATISTICS XML ON;
            SELECT COUNT(*) FROM @t t JOIN dbo.Big b ON b.Id = t.Id OPTION (USE HINT('QUERY_OPTIMIZER_COMPATIBILITY_LEVEL_{hintedLevel}'));
            SET STATISTICS XML OFF;
            """, connection);
        var plans = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            do
            {
                while (await reader.ReadAsync())
                {
                    if (reader.GetValue(0) is string text && text.Contains("ShowPlanXML", StringComparison.Ordinal))
                    {
                        plans.Add(text);
                    }
                }
            }
            while (await reader.NextResultAsync());
        }

        var plan = System.Xml.Linq.XDocument.Parse(Assert.Single(plans));
        var scan = plan.Descendants().Single(e => e.Name.LocalName == "RelOp" && (string?)e.Attribute("PhysicalOp") == "Table Scan");

        Assert.Equal(expectedRows, (double)scan.Attribute("EstimateRows")!);
    }
}
