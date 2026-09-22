using Microsoft.Data.SqlClient;
using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", "silentscan/predicates/isnull-replacement-value-truncation")]
public sealed class IsNullReplacementValueTruncationEngineFactOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(IsNullReplacementValueTruncationEngineFactOracleTests);

    protected override string Ddl => """
        CREATE TABLE dbo.T (
            Id INT IDENTITY PRIMARY KEY,
            ShortCode VARCHAR(5) NULL,
            LongCode VARCHAR(50) NULL,
            SmallDecimal DECIMAL(9, 2) NULL,
            BigDecimal DECIMAL(18, 6) NULL
        );
        """;

    [Fact]
    public async Task Isnull_ReplacementStringWiderThanCheckExpression_SilentlyTruncated_NoError()
    {
        await using var connection = await OpenConnectionAsync();

        await using (var insertCommand = new SqlCommand(
            "INSERT INTO dbo.T (LongCode) VALUES ('A much longer fallback description')",
            connection))
        {
            await insertCommand.ExecuteNonQueryAsync();
        }

        await using var selectCommand = new SqlCommand("SELECT ISNULL(ShortCode, LongCode) FROM dbo.T", connection);
        var result = await selectCommand.ExecuteScalarAsync();
        Assert.Equal("A muc", result?.ToString());
    }

    [Fact]
    public async Task Isnull_ReplacementDecimalWithMoreScaleThanCheckExpression_SilentlyRounded_NoError()
    {
        await using var connection = await OpenConnectionAsync();

        await using (var insertCommand = new SqlCommand(
            "INSERT INTO dbo.T (BigDecimal) VALUES (123.456789)",
            connection))
        {
            await insertCommand.ExecuteNonQueryAsync();
        }

        await using var selectCommand = new SqlCommand("SELECT CAST(ISNULL(SmallDecimal, BigDecimal) AS VARCHAR(20)) FROM dbo.T", connection);
        var result = await selectCommand.ExecuteScalarAsync();
        Assert.Equal("123.46", result?.ToString());
    }

    [Fact]
    public async Task Coalesce_ReplacementStringWiderThanCheckExpression_NotTruncated_ControlCase()
    {
        await using var connection = await OpenConnectionAsync();

        await using (var insertCommand = new SqlCommand(
            "INSERT INTO dbo.T (LongCode) VALUES ('A much longer fallback description')",
            connection))
        {
            await insertCommand.ExecuteNonQueryAsync();
        }

        await using var selectCommand = new SqlCommand("SELECT COALESCE(ShortCode, LongCode) FROM dbo.T", connection);
        var result = await selectCommand.ExecuteScalarAsync();
        Assert.Equal("A much longer fallback description", result?.ToString());
    }
}

[Trait("Category", "Oracle")]
[Trait("Rule", "silentscan/predicates/isnull-replacement-value-truncation")]
public sealed class IsNullReplacementValueTruncationScannerLiveOracleTests
{
    [Fact]
    public async Task LiveDeployment_ReplacementStringWiderThanCheckExpression_Fires()
    {
        var report = await EngineAuthoritativeScan.ScanAsync(
            """
            CREATE TABLE dbo.T (Id INT NOT NULL PRIMARY KEY, ShortCode VARCHAR(5) NULL, LongCode VARCHAR(50) NULL);
            GO
            CREATE PROCEDURE dbo.usp_IsnullWidening AS
            BEGIN
                SELECT ISNULL(ShortCode, LongCode) FROM dbo.T;
            END
            """,
            minimumConfidence: FindingConfidence.Low);

        var finding = Assert.Single(report.Find<IsNullReplacementValueTruncationFinding>("IsNullReplacementValueTruncationScanner"));
        Assert.Equal(WriteLossKind.LengthTruncation, finding.Kind);
    }

    [Fact]
    public async Task LiveDeployment_ReplacementDecimalWithMoreScaleThanCheckExpression_Fires()
    {
        var report = await EngineAuthoritativeScan.ScanAsync(
            """
            CREATE TABLE dbo.T (Id INT NOT NULL PRIMARY KEY, SmallDecimal DECIMAL(9, 2) NULL, BigDecimal DECIMAL(18, 6) NULL);
            GO
            CREATE PROCEDURE dbo.usp_IsnullScaleNarrowing AS
            BEGIN
                SELECT ISNULL(SmallDecimal, BigDecimal) FROM dbo.T;
            END
            """,
            minimumConfidence: FindingConfidence.Low);

        var finding = Assert.Single(report.Find<IsNullReplacementValueTruncationFinding>("IsNullReplacementValueTruncationScanner"));
        Assert.Equal(WriteLossKind.NumericScaleNarrowing, finding.Kind);
    }

    [Fact]
    public async Task LiveDeployment_CoalesceInsteadOfIsnull_NeverFires()
    {
        var report = await EngineAuthoritativeScan.ScanAsync(
            """
            CREATE TABLE dbo.T (Id INT NOT NULL PRIMARY KEY, ShortCode VARCHAR(5) NULL, LongCode VARCHAR(50) NULL);
            GO
            CREATE PROCEDURE dbo.usp_CoalesceWidening AS
            BEGIN
                SELECT COALESCE(ShortCode, LongCode) FROM dbo.T;
            END
            """,
            minimumConfidence: FindingConfidence.Low);

        Assert.Empty(report.Find<IsNullReplacementValueTruncationFinding>("IsNullReplacementValueTruncationScanner"));
    }
}
