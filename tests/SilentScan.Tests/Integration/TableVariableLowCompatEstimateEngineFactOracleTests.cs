using System.Text.RegularExpressions;
using System.Xml.Linq;
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
        CREATE TABLE dbo.Big (Id INT NOT NULL PRIMARY KEY, Pad CHAR(200) NOT NULL DEFAULT 'x');
        GO
        CREATE TABLE dbo.Dest (Id INT NOT NULL);
        GO
        """;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await ExecuteAsync(
            """
            INSERT INTO dbo.Big (Id)
            SELECT TOP (200000) n
            FROM (SELECT ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n FROM sys.all_objects a CROSS JOIN sys.all_objects b CROSS JOIN sys.all_objects c) x;
            """);
    }

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

    private async Task<(string Plain, string Recompile)> OperatorSequencesAsync(int rows, string statement)
    {
        var plain = await CaptureActualPlanAsync(BuildProbe(rows, statement));
        var recompile = await CaptureActualPlanAsync(BuildProbe(rows, $"{statement} OPTION (RECOMPILE)"));
        return (PhysicalOperators(plain), PhysicalOperators(recompile));
    }

    private static string BuildProbe(int rows, string statement) =>
        $"DECLARE @t TABLE (Id INT NOT NULL); INSERT INTO @t (Id) SELECT TOP ({rows}) Id FROM dbo.Big ORDER BY Id; {statement};";

    private static string PhysicalOperators(string planXml) =>
        string.Join(",", XDocument.Parse(planXml).Descendants()
            .Where(e => e.Name.LocalName == "RelOp")
            .Select(e => (string?)e.Attribute("PhysicalOp")));

    [Theory]
    [InlineData(60000, "SELECT b.Id FROM dbo.Big b JOIN @t t ON b.Id = t.Id")]
    [InlineData(1000, "SELECT Id, COUNT(*) AS c FROM @t GROUP BY Id")]
    [InlineData(1000, "SELECT DISTINCT Id FROM @t")]
    [InlineData(1000, "SELECT b.Id FROM dbo.Big b WHERE b.Id IN (SELECT Id FROM @t)")]
    [InlineData(1000, "SELECT b.Id FROM dbo.Big b WHERE EXISTS (SELECT 1 FROM @t t WHERE t.Id = b.Id)")]
    public async Task BelowCompat150_JoinedGroupedOrSemiJoinedTableVariable_RecompileChangesThePlan(int rows, string statement)
    {
        var (plain, recompile) = await OperatorSequencesAsync(rows, statement);

        Assert.NotEqual(plain, recompile);
    }

    [Theory]
    [InlineData(60000, "SELECT Id FROM @t")]
    [InlineData(60000, "SELECT Id FROM @t ORDER BY Id")]
    [InlineData(60000, "INSERT INTO dbo.Dest (Id) SELECT Id FROM @t")]
    [InlineData(1000, "SELECT COUNT(*) FROM @t")]
    [InlineData(1000, "SELECT SUM(Id) FROM @t")]
    [InlineData(1000, "SELECT TOP (10) Id FROM @t ORDER BY Id")]
    [InlineData(1000, "SELECT Id FROM @t WHERE Id > 5")]
    public async Task BelowCompat150_TableVariableAsOnlyRowSource_RecompileLeavesThePlanUnchanged(int rows, string statement)
    {
        var (plain, recompile) = await OperatorSequencesAsync(rows, statement);

        Assert.Equal(plain, recompile);
    }

    [GeneratedRegex("StatementText=\"SELECT Id FROM @t\"[^>]*StatementEstRows=\"([^\"]*)\"")]
    private static partial Regex PlainEstimateRegex();

    [GeneratedRegex("StatementText=\"SELECT Id FROM @t OPTION \\(RECOMPILE\\)\"[^>]*StatementEstRows=\"([^\"]*)\"")]
    private static partial Regex RecompileEstimateRegex();
}
