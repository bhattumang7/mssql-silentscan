using Microsoft.Data.SqlClient;
using SilentScan.Tests.Support;
using SilentScan.Verify.Oracle;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.LocalVariablePredicateRuleId)]
public sealed class LocalVariablePredicateOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(LocalVariablePredicateOracleTests);

    protected override string Ddl => """
        CREATE TABLE dbo.Customers (Id INT NOT NULL, Region VARCHAR(20) NOT NULL);
        GO
        CREATE INDEX IX_Customers_Region ON dbo.Customers(Region);
        GO
        CREATE TABLE dbo.Keyed (Id INT NOT NULL PRIMARY KEY, Payload VARCHAR(20) NOT NULL);
        GO
        """;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();

        await using var connection = new SqlConnection(Options.BuildConnectionString(DatabaseName));
        await connection.OpenAsync();

        await using var seedCommand = new SqlCommand(
            """
            INSERT INTO dbo.Customers (Id, Region)
            SELECT TOP (1900) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)), 'Common'
            FROM sys.all_objects a CROSS JOIN sys.all_objects b;

            INSERT INTO dbo.Customers (Id, Region)
            SELECT TOP (100) 1900 + ROW_NUMBER() OVER (ORDER BY (SELECT NULL)),
                   'Rare' + CAST(ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS VARCHAR(10))
            FROM sys.all_objects a CROSS JOIN sys.all_objects b;

            INSERT INTO dbo.Keyed (Id, Payload)
            SELECT TOP (2000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)), 'x'
            FROM sys.all_objects a CROSS JOIN sys.all_objects b;

            UPDATE STATISTICS dbo.Customers WITH FULLSCAN;
            UPDATE STATISTICS dbo.Keyed WITH FULLSCAN;
            """, connection);
        await seedCommand.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task SniffedLiteral_VsDeclaredLocalVariable_ProduceMateriallyDifferentEstimates()
    {
        var capture = new PlanXmlCapture(Options);

        var literalPlan = await capture.CaptureAsync(
            DatabaseName, "SELECT Id FROM dbo.Customers WHERE Region = 'Common';");

        var localVariablePlan = await capture.CaptureAsync(
            DatabaseName, "DECLARE @p VARCHAR(20) = 'Common'; SELECT Id FROM dbo.Customers WHERE Region = @p;");

        var literalEstimate = ExtractEstimateRows(literalPlan);
        var localVariableEstimate = ExtractEstimateRows(localVariablePlan);

        Assert.True(literalEstimate > 1500, $"expected the literal probe's estimate to reflect the real skew (~1900), got {literalEstimate}.");
        Assert.True(
            localVariableEstimate < literalEstimate / 2,
            $"expected the local-variable probe's estimate ({localVariableEstimate}) to diverge materially from the literal probe's ({literalEstimate}) - if they're close, the estimator premise this finding relies on no longer holds.");
    }

    [Fact]
    public async Task OptimizeForNamingTheVariableValue_RestoresTheHistogramEstimate_OtherHintsDoNot()
    {
        var capture = new PlanXmlCapture(Options);
        const string Declare = "DECLARE @p VARCHAR(20) = 'Common'; DECLARE @q VARCHAR(20) = 'x'; ";
        const string Query = "SELECT Id FROM dbo.Customers WHERE Region = @p AND @q IS NOT NULL ";

        var named = await capture.CaptureAsync(DatabaseName, Declare + Query + "OPTION (OPTIMIZE FOR (@p = 'Common', @q = 'x'));");
        var unknown = await capture.CaptureAsync(DatabaseName, Declare + Query + "OPTION (OPTIMIZE FOR (@p UNKNOWN, @q = 'x'));");
        var otherVariable = await capture.CaptureAsync(DatabaseName, Declare + Query + "OPTION (OPTIMIZE FOR (@q = 'x'));");

        Assert.True(ExtractEstimateRows(named) > 1500, "a named OPTIMIZE FOR value must drive the histogram estimate.");
        Assert.True(ExtractEstimateRows(unknown) < 1000, "OPTIMIZE FOR UNKNOWN keeps the density estimate.");
        Assert.True(ExtractEstimateRows(otherVariable) < 1000, "OPTIMIZE FOR on another variable leaves this variable on the density estimate.");
    }

    [Fact]
    public async Task UniqueKeyEquality_LocalVariableAndSniffedLiteralEstimateTheSameRow()
    {
        var capture = new PlanXmlCapture(Options);

        var literalPlan = await capture.CaptureAsync(
            DatabaseName, "SELECT Payload FROM dbo.Keyed WHERE Id = 7;");

        var localVariablePlan = await capture.CaptureAsync(
            DatabaseName, "DECLARE @p INT = 7; SELECT Payload FROM dbo.Keyed WHERE Id = @p;");

        Assert.Equal(1d, ExtractEstimateRows(literalPlan));
        Assert.Equal(1d, ExtractEstimateRows(localVariablePlan));
    }

    private static double ExtractEstimateRows(string planXml)
    {

        const string Marker = "EstimateRows=\"";
        var start = planXml.IndexOf(Marker, StringComparison.Ordinal);
        Assert.True(start >= 0, "expected the plan XML to contain an EstimateRows attribute.");
        start += Marker.Length;
        var end = planXml.IndexOf('"', start);
        return double.Parse(planXml[start..end], System.Globalization.CultureInfo.InvariantCulture);
    }
}
