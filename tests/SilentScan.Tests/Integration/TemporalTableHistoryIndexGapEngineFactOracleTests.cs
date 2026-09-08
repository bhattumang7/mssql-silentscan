using Microsoft.Data.SqlClient;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class TemporalTableHistoryIndexGapEngineFactOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(TemporalTableHistoryIndexGapEngineFactOracleTests);

    protected override string Ddl => """
        CREATE TABLE dbo.Widget
        (
            Id   INT NOT NULL PRIMARY KEY,
            Code VARCHAR(20) NOT NULL,
            ValidFrom DATETIME2 GENERATED ALWAYS AS ROW START,
            ValidTo   DATETIME2 GENERATED ALWAYS AS ROW END,
            PERIOD FOR SYSTEM_TIME (ValidFrom, ValidTo)
        )
        WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = dbo.WidgetHistory));
        GO
        CREATE NONCLUSTERED INDEX IX_Widget_Code ON dbo.Widget (Code);
        GO
        INSERT INTO dbo.Widget (Id, Code)
        SELECT ROW_NUMBER() OVER (ORDER BY (SELECT NULL)), 'C' + CAST(ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS VARCHAR(10))
        FROM sys.objects a CROSS JOIN sys.objects b;
        GO
        UPDATE dbo.Widget SET Code = Code + 'x' WHERE Id <= 2500;
        GO
        UPDATE STATISTICS dbo.Widget WITH FULLSCAN;
        UPDATE STATISTICS dbo.WidgetHistory WITH FULLSCAN;
        GO
        """;

    private const string Query = """
        SELECT * FROM dbo.Widget FOR SYSTEM_TIME BETWEEN '2020-01-01' AND '2030-12-31' WHERE Code = 'ABC';
        """;

    private static async Task<string> CaptureTemporalQueryPlanAsync(SqlConnection connection)
    {
        await using (var onCommand = new SqlCommand("SET STATISTICS XML ON;", connection))
        {
            await onCommand.ExecuteNonQueryAsync();
        }

        var planXmlBuilder = new System.Text.StringBuilder();
        await using (var probeCommand = new SqlCommand(Query, connection))
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
    public async Task ForSystemTimeQuery_RewritesToUnionAllOfCurrentAndHistoryTables()
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(DatabaseName));
        await connection.OpenAsync();

        var planXml = await CaptureTemporalQueryPlanAsync(connection);

        Assert.Contains("PhysicalOp=\"Concatenation\"", planXml, StringComparison.Ordinal);
        Assert.Contains("Table=\"[Widget]\"", planXml, StringComparison.Ordinal);
        Assert.Contains("Table=\"[WidgetHistory]\"", planXml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithNoMatchingHistoryIndex_TheHistoryBranchScansWhileTheCurrentBranchSeeks()
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(DatabaseName));
        await connection.OpenAsync();

        var planXml = await CaptureTemporalQueryPlanAsync(connection);

        Assert.Contains("PhysicalOp=\"Index Seek\"", planXml, StringComparison.Ordinal);
        Assert.Contains("PhysicalOp=\"Clustered Index Scan\"", planXml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithAStructurallyMatchingHistoryIndex_BothBranchesSeek()
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(DatabaseName));
        await connection.OpenAsync();

        await using (var indexCommand = connection.CreateCommand())
        {
            indexCommand.CommandText = """
                CREATE NONCLUSTERED INDEX IX_WidgetHistory_Code ON dbo.WidgetHistory (Code);
                UPDATE STATISTICS dbo.WidgetHistory WITH FULLSCAN;
                """;
            await indexCommand.ExecuteNonQueryAsync();
        }

        var planXml = await CaptureTemporalQueryPlanAsync(connection);

        Assert.DoesNotContain("PhysicalOp=\"Clustered Index Scan\"", planXml, StringComparison.Ordinal);
        var seekCount = System.Text.RegularExpressions.Regex.Count(planXml, "PhysicalOp=\"Index Seek\"");
        Assert.Equal(2, seekCount);
    }
}
