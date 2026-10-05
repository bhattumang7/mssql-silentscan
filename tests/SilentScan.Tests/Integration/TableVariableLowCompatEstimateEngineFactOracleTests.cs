using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
[Trait("Rule", "silentscan/query/table-variable-low-compat-estimate")]
public sealed partial class TableVariableLowCompatEstimateEngineFactOracleTests : OracleTestFixture
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
            SELECT Id FROM @t;
            SELECT Id FROM @t OPTION (RECOMPILE);
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

        var plainStatementMatch = PlainEstimateRegex().Match(planXml);
        Assert.True(plainStatementMatch.Success, planXml);
        Assert.Equal("1", plainStatementMatch.Groups[1].Value);
    }

    [Fact]
    public async Task BelowCompat150_StatementWithOptionRecompile_EstimatesTheRealRowCount()
    {
        var planXml = await CaptureTableVariableCountPlanAsync();

        var recompileStatementMatch = RecompileEstimateRegex().Match(planXml);
        Assert.True(recompileStatementMatch.Success, planXml);
        Assert.Equal("1000", recompileStatementMatch.Groups[1].Value);
    }

    [GeneratedRegex("StatementText=\"SELECT Id FROM @t\"[^>]*StatementEstRows=\"([^\"]*)\"")]
    private static partial Regex PlainEstimateRegex();

    [GeneratedRegex("StatementText=\"SELECT Id FROM @t OPTION \\(RECOMPILE\\)\"[^>]*StatementEstRows=\"([^\"]*)\"")]
    private static partial Regex RecompileEstimateRegex();
}
