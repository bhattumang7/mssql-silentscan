using Microsoft.Data.SqlClient;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.ForcedSerialFastForwardCursorRuleId)]
public sealed class FastForwardCursorOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(FastForwardCursorOracleTests);

    protected override string Ddl => """
        CREATE TABLE dbo.BigTable (Id INT NOT NULL, Grp INT NOT NULL, Val VARCHAR(100) NOT NULL);
        GO
        """;

    protected override async Task SeedAsync()
    {

        await using var connection = new SqlConnection(Options.BuildConnectionString(DatabaseName));
        await connection.OpenAsync();

        await using var seedCommand = new SqlCommand(
            """
            INSERT INTO dbo.BigTable (Id, Grp, Val)
            SELECT TOP (200000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)),
                   ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) % 100, REPLICATE('x', 50)
            FROM sys.all_objects a CROSS JOIN sys.all_objects b;
            UPDATE STATISTICS dbo.BigTable WITH FULLSCAN;
            """, connection);
        await seedCommand.ExecuteNonQueryAsync();
    }

    private async Task<string> CaptureCursorDefiningQueryPlanAsync(
        string cursorOptions, string definingQuery = "SELECT Grp, COUNT(*) FROM dbo.BigTable GROUP BY Grp")
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(DatabaseName));
        await connection.OpenAsync();

        await using (var onCommand = new SqlCommand("SET STATISTICS XML ON;", connection))
        {
            await onCommand.ExecuteNonQueryAsync();
        }

        var probe =
            $"""
            DECLARE c CURSOR {cursorOptions} FOR {definingQuery};
            OPEN c;
            FETCH NEXT FROM c;
            CLOSE c;
            DEALLOCATE c;
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
    public async Task FastForward_ForcesSerial()
    {
        var planXml = await CaptureCursorDefiningQueryPlanAsync("FAST_FORWARD");

        Assert.Contains("NonParallelPlanReason=\"NoParallelFastForwardCursor\"", planXml);
    }

    [Fact]
    public async Task BareForwardOnlyReadOnly_ForcesSerial()
    {
        var planXml = await CaptureCursorDefiningQueryPlanAsync("FORWARD_ONLY READ_ONLY");

        Assert.Contains("NonParallelPlanReason=\"NoParallelFastForwardCursor\"", planXml);
    }

    [Fact]
    public async Task LocalStaticForwardOnlyReadOnly_NeverForcesSerial()
    {
        var planXml = await CaptureCursorDefiningQueryPlanAsync("LOCAL STATIC FORWARD_ONLY READ_ONLY");

        Assert.DoesNotContain("NonParallelPlanReason=\"NoParallelFastForwardCursor\"", planXml);
    }

    [Fact]
    public async Task Dynamic_NeverForcesSerial()
    {
        var planXml = await CaptureCursorDefiningQueryPlanAsync("DYNAMIC");

        Assert.DoesNotContain("NonParallelPlanReason=\"NoParallelFastForwardCursor\"", planXml);
    }

    [Theory]
    [InlineData("SELECT name FROM sys.objects")]
    [InlineData("SELECT ROUTINE_NAME FROM INFORMATION_SCHEMA.ROUTINES")]
    [InlineData("SELECT o.name FROM sys.objects o JOIN dbo.BigTable b ON b.Id = o.object_id")]
    public async Task FastForward_OverASystemCatalogView_ReportsNoFastForwardReason(string definingQuery)
    {
        var planXml = await CaptureCursorDefiningQueryPlanAsync("LOCAL FAST_FORWARD", definingQuery);

        Assert.DoesNotContain("NonParallelPlanReason=\"NoParallelFastForwardCursor\"", planXml);
    }

    [Theory]
    [InlineData("SELECT Id FROM dbo.BigTable")]
    [InlineData("SELECT Id FROM dbo.BigTable WHERE Id = 5")]
    public async Task FastForward_OverAUserTable_ReportsTheFastForwardReason(string definingQuery)
    {
        var planXml = await CaptureCursorDefiningQueryPlanAsync("LOCAL FAST_FORWARD", definingQuery);

        Assert.Contains("NonParallelPlanReason=\"NoParallelFastForwardCursor\"", planXml);
    }

    [Fact]
    public async Task NoOptions_NeverForcesSerial()
    {
        var planXml = await CaptureCursorDefiningQueryPlanAsync(string.Empty);

        Assert.DoesNotContain("NonParallelPlanReason=\"NoParallelFastForwardCursor\"", planXml);
    }

    private async Task<IReadOnlyList<string>> CapturePlansAsync(string batch)
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(DatabaseName));
        await connection.OpenAsync();

        var plans = new List<string>();
        await using var command = new SqlCommand(batch, connection) { CommandTimeout = 300 };
        await using var reader = await command.ExecuteReaderAsync();
        do
        {
            while (await reader.ReadAsync())
            {
                if (reader.FieldCount == 1 && reader.GetFieldType(0) == typeof(string))
                {
                    var value = reader.GetString(0);
                    if (value.Contains("ShowPlanXML", StringComparison.Ordinal))
                    {
                        plans.Add(value);
                    }
                }
            }
        }
        while (await reader.NextResultAsync());

        return plans;
    }

    [Fact]
    public async Task BareTableVariableScan_NeverGoesParallel_WhileAFilteredScanOfTheSameTableVariableDoes()
    {
        var plans = await CapturePlansAsync(
            """
            SET NOCOUNT ON;
            DECLARE @t TABLE (a INT PRIMARY KEY, b INT, c VARCHAR(50));
            INSERT @t
            SELECT TOP (1500000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)), 1, REPLICATE('x', 40)
            FROM sys.all_objects x, sys.all_objects y, sys.all_objects z;
            SET STATISTICS XML ON;
            SELECT a, b, c FROM @t OPTION (USE HINT('ENABLE_PARALLEL_PLAN_PREFERENCE'));
            SELECT a FROM @t OPTION (USE HINT('ENABLE_PARALLEL_PLAN_PREFERENCE'));
            SELECT a FROM @t WHERE b % 7 = 3 AND c LIKE '%y%';
            SET STATISTICS XML OFF;
            """);

        Assert.Equal(3, plans.Count);
        Assert.DoesNotContain("<Parallelism", plans[0], StringComparison.Ordinal);
        Assert.DoesNotContain("<Parallelism", plans[1], StringComparison.Ordinal);
        Assert.Contains("<Parallelism", plans[2], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("LOCAL READ_ONLY", "SELECT Id FROM dbo.BigTable", true)]
    [InlineData("", "SELECT Id FROM dbo.BigTable FOR READ ONLY", true)]
    [InlineData("LOCAL SCROLL READ_ONLY", "SELECT Id FROM dbo.BigTable", false)]
    [InlineData("INSENSITIVE", "SELECT Id FROM dbo.BigTable FOR READ ONLY", false)]
    public async Task ReadOnlyAloneIsAFastForwardCursor(string options, string definingQuery, bool fastForward)
    {
        var cursor = options == "INSENSITIVE" ? "c INSENSITIVE CURSOR" : $"c CURSOR {options}";
        await using var connection = new SqlConnection(Options.BuildConnectionString(DatabaseName));
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            $"DECLARE {cursor} FOR {definingQuery}; OPEN c; SELECT properties FROM sys.dm_exec_cursors(@@SPID); CLOSE c; DEALLOCATE c;",
            connection);

        var properties = (string)(await command.ExecuteScalarAsync())!;

        Assert.Equal(fastForward, properties.Contains("Fast_Forward", StringComparison.Ordinal));
    }
}
