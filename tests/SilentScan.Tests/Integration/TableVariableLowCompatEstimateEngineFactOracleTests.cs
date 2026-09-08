using Microsoft.Data.SqlClient;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class TableVariableLowCompatEstimateEngineFactOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(TableVariableLowCompatEstimateEngineFactOracleTests);

    protected override string Ddl => """
        ALTER DATABASE CURRENT SET COMPATIBILITY_LEVEL = 140;
        GO
        """;

    private async Task<string> CaptureTableVariableCountPlanAsync()
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(DatabaseName));
        await connection.OpenAsync();

        await using (var onCommand = new SqlCommand("SET STATISTICS XML ON;", connection))
        {
            await onCommand.ExecuteNonQueryAsync();
        }

        const string probe = """
            DECLARE @t TABLE (Id INT NOT NULL);
            INSERT INTO @t (Id)
            SELECT TOP (1000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL))
            FROM sys.objects a CROSS JOIN sys.objects b;
            SELECT COUNT(*) FROM @t;
            """;

        var planXmlBuilder = new System.Text.StringBuilder();
        await using (var probeCommand = new SqlCommand(probe, connection))
        await using (var reader = await probeCommand.ExecuteReaderAsync())
        {
            do
            {
                while (await reader.ReadAsync())
                {
                    if (reader.FieldCount == 1 && reader.GetFieldType(0) == typeof(string))
                    {
                        var value = reader.GetString(0);
                        if (value.Contains("ShowPlanXML", StringComparison.Ordinal))
                        {
                            planXmlBuilder.Append(value);
                        }
                    }
                }
            }
            while (await reader.NextResultAsync());
        }

        await using (var offCommand = new SqlCommand("SET STATISTICS XML OFF;", connection))
        {
            await offCommand.ExecuteNonQueryAsync();
        }

        var planXml = planXmlBuilder.ToString();
        Assert.NotEmpty(planXml);
        return planXml;
    }

    [Fact]
    public async Task BelowCompat150_TableVariableWith1000RealRows_StillEstimatesExactlyOneRow()
    {
        var planXml = await CaptureTableVariableCountPlanAsync();

        var countStatementMatch = System.Text.RegularExpressions.Regex.Match(
            planXml,
            "StatementText=\"SELECT COUNT\\(\\*\\) FROM @t\"[^>]*StatementEstRows=\"([^\"]*)\"");
        Assert.True(countStatementMatch.Success, planXml);
        Assert.Equal("1", countStatementMatch.Groups[1].Value);
    }
}
