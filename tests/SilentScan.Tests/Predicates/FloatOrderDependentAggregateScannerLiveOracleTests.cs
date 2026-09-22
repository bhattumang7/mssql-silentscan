using Microsoft.Data.SqlClient;
using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", "silentscan/predicates/float-order-dependent-aggregate")]
public sealed class FloatOrderDependentAggregateEngineFactOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(FloatOrderDependentAggregateEngineFactOracleTests);

    protected override string Ddl => """
        CREATE TABLE dbo.Measurements (Id INT IDENTITY PRIMARY KEY, Amount FLOAT NOT NULL);
        GO
        INSERT INTO dbo.Measurements (Amount)
        SELECT 0.1 + (ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) % 7) * 1e-16
        FROM sys.all_objects a CROSS JOIN sys.all_objects b;
        GO
        UPDATE STATISTICS dbo.Measurements WITH FULLSCAN;
        GO
        """;

    [Fact]
    public async Task SumOverFloatColumn_RealBitPatternDiffersBetweenSerialAndForcedParallelPlan()
    {
        await using var connection = await OpenConnectionAsync();

        await using var serialCommand = new SqlCommand(
            "SELECT CAST(SUM(Amount) AS VARBINARY(8)) FROM dbo.Measurements OPTION (MAXDOP 1);",
            connection)
        { CommandTimeout = 120 };
        var serialBits = (byte[])(await serialCommand.ExecuteScalarAsync())!;

        await using var parallelCommand = new SqlCommand(
            "SELECT CAST(SUM(Amount) AS VARBINARY(8)) FROM dbo.Measurements OPTION (MAXDOP 4, QUERYTRACEON 8649);",
            connection)
        { CommandTimeout = 120 };
        var parallelBits = (byte[])(await parallelCommand.ExecuteScalarAsync())!;

        Assert.NotEqual(Convert.ToHexString(serialBits), Convert.ToHexString(parallelBits));
    }
}

[Trait("Category", "Oracle")]
[Trait("Rule", "silentscan/predicates/float-order-dependent-aggregate")]
public sealed class FloatOrderDependentAggregateScannerLiveOracleTests
{
    [Fact]
    public async Task LiveDeployment_SumOverArithmeticExpressionOfFloatColumn_Fires()
    {
        var report = await EngineAuthoritativeScan.ScanAsync(
            """
            CREATE TABLE dbo.Measurements (Id INT NOT NULL PRIMARY KEY, Amount FLOAT NOT NULL);
            GO
            CREATE PROCEDURE dbo.usp_SumArithmeticFloat AS
            BEGIN
                SELECT SUM(Amount * 2) FROM dbo.Measurements;
            END
            """,
            minimumConfidence: FindingConfidence.Low);

        var finding = Assert.Single(report.Find<FloatOrderDependentAggregateFinding>("FloatOrderDependentAggregateScanner"));
        Assert.Equal("dbo.Measurements", finding.TableQualifiedName);
        Assert.Equal("Amount", finding.ColumnName);
        Assert.Equal("SUM", finding.AggregateFunctionName);
    }

    [Fact]
    public async Task LiveDeployment_SumOverIntegerColumnCastToFloat_Fires()
    {
        var report = await EngineAuthoritativeScan.ScanAsync(
            """
            CREATE TABLE dbo.Measurements (Id INT NOT NULL PRIMARY KEY, Quantity INT NOT NULL);
            GO
            CREATE PROCEDURE dbo.usp_SumCastToFloat AS
            BEGIN
                SELECT SUM(CAST(Quantity AS FLOAT)) FROM dbo.Measurements;
            END
            """,
            minimumConfidence: FindingConfidence.Low);

        var finding = Assert.Single(report.Find<FloatOrderDependentAggregateFinding>("FloatOrderDependentAggregateScanner"));
        Assert.Equal("Quantity", finding.ColumnName);
    }

    [Fact]
    public async Task LiveDeployment_SumOverIntegerColumnPlusFloatLiteralConstant_Fires()
    {
        var report = await EngineAuthoritativeScan.ScanAsync(
            """
            CREATE TABLE dbo.Measurements (Id INT NOT NULL PRIMARY KEY, Quantity INT NOT NULL);
            GO
            CREATE PROCEDURE dbo.usp_SumPlusFloatConstant AS
            BEGIN
                SELECT SUM(Quantity + 1.5e0) FROM dbo.Measurements;
            END
            """,
            minimumConfidence: FindingConfidence.Low);

        var finding = Assert.Single(report.Find<FloatOrderDependentAggregateFinding>("FloatOrderDependentAggregateScanner"));
        Assert.Equal("Quantity", finding.ColumnName);
    }

    [Fact]
    public async Task LiveDeployment_SumOverIntegerColumnCastToDecimal_NeverFires()
    {
        var report = await EngineAuthoritativeScan.ScanAsync(
            """
            CREATE TABLE dbo.Measurements (Id INT NOT NULL PRIMARY KEY, Amount FLOAT NOT NULL);
            GO
            CREATE PROCEDURE dbo.usp_SumCastToDecimal AS
            BEGIN
                SELECT SUM(CAST(Amount AS DECIMAL(18, 4))) FROM dbo.Measurements;
            END
            """,
            minimumConfidence: FindingConfidence.Low);

        Assert.Empty(report.Find<FloatOrderDependentAggregateFinding>("FloatOrderDependentAggregateScanner"));
    }

    [Fact]
    public async Task LiveDeployment_SumOverIntegerColumnPlusIntegerLiteral_NeverFires()
    {
        var report = await EngineAuthoritativeScan.ScanAsync(
            """
            CREATE TABLE dbo.Measurements (Id INT NOT NULL PRIMARY KEY, Quantity INT NOT NULL);
            GO
            CREATE PROCEDURE dbo.usp_SumPlusIntegerLiteral AS
            BEGIN
                SELECT SUM(Quantity + 1) FROM dbo.Measurements;
            END
            """,
            minimumConfidence: FindingConfidence.Low);

        Assert.Empty(report.Find<FloatOrderDependentAggregateFinding>("FloatOrderDependentAggregateScanner"));
    }
}
