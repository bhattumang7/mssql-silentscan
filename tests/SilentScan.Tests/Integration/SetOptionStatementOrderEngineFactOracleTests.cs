using Microsoft.Data.SqlClient;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.SetOptionAnsiWarningsOffBlocksIndexedFeatureRuleId)]
public sealed class SetOptionStatementOrderEngineFactOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(SetOptionStatementOrderEngineFactOracleTests);

    protected override string Ddl => """
        CREATE TABLE dbo.F (Id INT PRIMARY KEY, Status INT NOT NULL, K INT NOT NULL);
        INSERT INTO dbo.F SELECT TOP (20000) ROW_NUMBER() OVER (ORDER BY (SELECT 1)), CASE WHEN ROW_NUMBER() OVER (ORDER BY (SELECT 1)) % 100 = 0 THEN 1 ELSE 0 END, ROW_NUMBER() OVER (ORDER BY (SELECT 1)) FROM sys.all_objects a, sys.all_objects b;
        CREATE INDEX IX_F ON dbo.F(K) WHERE Status = 1;
        GO
        CREATE PROCEDURE dbo.P_OffBefore AS BEGIN SET ANSI_WARNINGS OFF; SELECT K FROM dbo.F WHERE Status = 1 AND K = 500; END;
        GO
        CREATE PROCEDURE dbo.P_OffAfter AS BEGIN SELECT K FROM dbo.F WHERE Status = 1 AND K = 500; SET ANSI_WARNINGS OFF; END;
        GO
        CREATE PROCEDURE dbo.P_OffThenOn AS BEGIN SET ANSI_WARNINGS OFF; SET ANSI_WARNINGS ON; SELECT K FROM dbo.F WHERE Status = 1 AND K = 500; END;
        GO
        """;

    [Theory]
    [InlineData("P_OffBefore", false)]
    [InlineData("P_OffAfter", true)]
    [InlineData("P_OffThenOn", true)]
    public async Task AnsiWarningsOffBlocksTheFilteredIndexOnlyForStatementsAfterIt(string procedure, bool usesIndex)
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(DatabaseName));
        await connection.OpenAsync();
        await using (var on = new SqlCommand("SET STATISTICS XML ON;", connection))
        {
            await on.ExecuteNonQueryAsync();
        }

        var plans = new System.Text.StringBuilder();
        await using (var exec = new SqlCommand($"EXEC dbo.{procedure};", connection))
        await using (var reader = await exec.ExecuteReaderAsync())
        {
            do
            {
                while (await reader.ReadAsync())
                {
                    if (reader.FieldCount == 1 && reader.GetFieldType(0) == typeof(string) && reader.GetString(0).Contains("ShowPlanXML", StringComparison.Ordinal))
                    {
                        plans.Append(reader.GetString(0));
                    }
                }
            }
            while (await reader.NextResultAsync());
        }

        Assert.Equal(usesIndex, plans.ToString().Contains("Index=\"[IX_F]\"", StringComparison.Ordinal));
    }
}
