using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class TableVariableStaleEstimateInLoopEngineFactOracleTests : OracleTestFixture
{
    private const int LoopIterations = 200;

    protected override string DatabaseNameSeed => nameof(TableVariableStaleEstimateInLoopEngineFactOracleTests);

    protected override string Ddl => """
        CREATE TABLE dbo.SourceRows (RowId INT NOT NULL PRIMARY KEY, Amount DECIMAL(10,2) NOT NULL);
        INSERT INTO dbo.SourceRows (RowId, Amount)
        SELECT TOP (200) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)), 1.0
        FROM sys.objects a CROSS JOIN sys.objects b;
        GO
        """;

    [Fact]
    public async Task ReadStatementInsideGrowingLoop_KeepsEstimatingOneRowWhileActualRowsClimbsToTheLoopCount()
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(DatabaseName));
        await connection.OpenAsync();

        await using (var onCommand = new SqlCommand("SET STATISTICS XML ON;", connection))
        {
            await onCommand.ExecuteNonQueryAsync();
        }

        var probe =
            $"""
            DECLARE @Accumulator TABLE (RowId INT NOT NULL, Amount DECIMAL(10,2) NOT NULL);
            DECLARE @NextRowId INT = 1;
            DECLARE @total DECIMAL(18,2);
            WHILE @NextRowId <= {LoopIterations}
            BEGIN
                INSERT INTO @Accumulator (RowId, Amount)
                SELECT RowId, Amount FROM dbo.SourceRows WHERE RowId = @NextRowId;

                SELECT @total = SUM(Amount) FROM @Accumulator;

                SET @NextRowId += 1;
            END;
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

        var estimateMatches = Regex.Matches(
            planXml,
            "StatementText=\"SELECT @total = SUM\\(Amount\\) FROM @Accumulator\"[^>]*StatementEstRows=\"([^\"]*)\"");
        Assert.True(estimateMatches.Count > 1, planXml);
        Assert.All(estimateMatches, m => Assert.Equal("1", m.Groups[1].Value));

        var actualRowMatches = Regex.Matches(planXml, "ActualRows=\"([0-9]+)\"");
        var maxActualRows = actualRowMatches.Select(m => int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)).Max();
        Assert.True(
            maxActualRows >= LoopIterations - 10,
            $"expected actual rows to climb near {LoopIterations}, but the highest observed was {maxActualRows}");
    }
}
