using Microsoft.Data.SqlClient;
using SilentScan.Core.Catalog;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.FloatEqualityRuleId)]
public sealed class FloatEqualityPredicateScannerOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(FloatEqualityPredicateScannerOracleTests);

    protected override string Ddl => """
        CREATE TABLE dbo.SensorReadings (ReadingId INT NOT NULL PRIMARY KEY, Threshold FLOAT NOT NULL);
        GO
        CREATE TABLE dbo.ExactReadings (ReadingId INT NOT NULL PRIMARY KEY, Threshold DECIMAL(10, 4) NOT NULL);
        GO
        """;

    [Fact]
    public async Task FloatNotEqualPredicate_AgainstValueAPersonWouldCallTheSame_EngineReportsUnequal_ScannerMustFlag()
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(DatabaseName));
        await connection.OpenAsync();

        await using var command = new SqlCommand(
            "SELECT CASE WHEN (CAST(0.1 AS FLOAT) + CAST(0.2 AS FLOAT)) <> CAST(0.3 AS FLOAT) THEN 1 ELSE 0 END;",
            connection);
        var engineResult = (int)(await command.ExecuteScalarAsync())!;
        Assert.Equal(1, engineResult);

        var findings = Scan("SELECT ReadingId FROM dbo.SensorReadings WHERE Threshold <> 0.3;");

        var finding = Assert.Single(findings);
        Assert.Equal("Threshold", finding.ColumnName);
    }

    [Fact]
    public async Task FloatColumnHoldingASumOfFloats_DoesNotMatchTheEqualityLiteral_ButDecimalColumnDoes()
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(DatabaseName));
        await connection.OpenAsync();

        await using (var seed = new SqlCommand(
            "INSERT INTO dbo.SensorReadings (ReadingId, Threshold) VALUES (1, CAST(0.1 AS FLOAT) + CAST(0.2 AS FLOAT));"
            + "INSERT INTO dbo.ExactReadings (ReadingId, Threshold) VALUES (1, 0.1 + 0.2);", connection))
        {
            await seed.ExecuteNonQueryAsync();
        }

        await using var floatCount = new SqlCommand("SELECT COUNT(*) FROM dbo.SensorReadings WHERE Threshold = 0.3;", connection);
        await using var decimalCount = new SqlCommand("SELECT COUNT(*) FROM dbo.ExactReadings WHERE Threshold = 0.3;", connection);

        Assert.Equal(0, (int)(await floatCount.ExecuteScalarAsync())!);
        Assert.Equal(1, (int)(await decimalCount.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task FloatColumnHoldingADecimalLiteralSum_StillMatches_SoTheDocExampleMustUseFloatArithmetic()
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(DatabaseName));
        await connection.OpenAsync();

        await using (var seed = new SqlCommand("INSERT INTO dbo.SensorReadings (ReadingId, Threshold) VALUES (2, 0.1 + 0.2);", connection))
        {
            await seed.ExecuteNonQueryAsync();
        }

        await using var count = new SqlCommand("SELECT COUNT(*) FROM dbo.SensorReadings WHERE ReadingId = 2 AND Threshold = 0.3;", connection);

        Assert.Equal(1, (int)(await count.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task IntegerNotEqualPredicate_NeverSubjectToFloatImprecision_ScannerMustNotFlag()
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(DatabaseName));
        await connection.OpenAsync();

        await using var command = new SqlCommand(
            "SELECT CASE WHEN (1 + 2) <> 3 THEN 1 ELSE 0 END;",
            connection);
        var engineResult = (int)(await command.ExecuteScalarAsync())!;
        Assert.Equal(0, engineResult);

        var findings = Scan("SELECT ReadingId FROM dbo.SensorReadings WHERE ReadingId <> 3;");

        Assert.Empty(findings);
    }

    private static IReadOnlyList<FloatEqualityFinding> Scan(string sql)
    {
        var ddl = "CREATE TABLE dbo.SensorReadings (ReadingId INT NOT NULL PRIMARY KEY, Threshold FLOAT NOT NULL);";
        var parsed = SqlScriptParser.ParseText("test.sql", $"{ddl}\nGO\n{sql}");
        Assert.False(parsed.HasErrors, string.Join("; ", parsed.Errors.Select(e => e.Message)));

        var catalog = CatalogBuilder.Build([parsed]);
        return FloatEqualityPredicateScanner.Scan(parsed, catalog);
    }
}
