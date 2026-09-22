using SilentScan.Verify;
using SilentScan.Verify.Deployment;
using SilentScan.Verify.Oracle;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
[Trait("Rule", "silentscan/tier1/cast-or-convert-on-column")]
public sealed class CastOrConvertOnColumnOracleTests : IAsyncLifetime
{
    private const string DatabaseName = "SilentScanCastOrConvertOracleTest";

    private readonly SqlServerOptions _options = SqlServerOptions.LocalDocker;
    private readonly DatabaseProvisioner _provisioner;

    public CastOrConvertOnColumnOracleTests()
    {
        _provisioner = new DatabaseProvisioner(_options);
    }

    public async Task InitializeAsync()
    {
        await _provisioner.CreateFreshAsync(DatabaseName);
        await new ScriptDeployer(_options).DeployAsync(
            """
            CREATE TABLE dbo.Orders (Code VARCHAR(20) NOT NULL, OrderNo INT NOT NULL, PlacedAt DATETIME NOT NULL, Amount DECIMAL(18,4) NOT NULL);
            GO
            CREATE INDEX IX_Orders_Code ON dbo.Orders(Code);
            GO
            CREATE INDEX IX_Orders_OrderNo ON dbo.Orders(OrderNo);
            GO
            CREATE INDEX IX_Orders_PlacedAt ON dbo.Orders(PlacedAt);
            GO
            CREATE INDEX IX_Orders_Amount ON dbo.Orders(Amount);
            GO
            INSERT INTO dbo.Orders(Code, OrderNo, PlacedAt, Amount)
            SELECT TOP (5000) 'C' + CAST(ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS VARCHAR(10)),
                   ROW_NUMBER() OVER (ORDER BY (SELECT NULL)),
                   DATEADD(MINUTE, ROW_NUMBER() OVER (ORDER BY (SELECT NULL)), '2020-01-01'),
                   ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) + 0.1234
            FROM sys.all_objects a CROSS JOIN sys.all_objects b;
            GO
            UPDATE STATISTICS dbo.Orders WITH FULLSCAN;
            GO
            CREATE PROCEDURE dbo.ProbeCastChangesCategory @x VARCHAR(20) AS
            BEGIN
                SELECT Code FROM dbo.Orders WHERE CAST(OrderNo AS VARCHAR(20)) = @x;
            END
            GO
            CREATE PROCEDURE dbo.ProbeNoOpConvertOnNumeric @x INT AS
            BEGIN
                SELECT Code FROM dbo.Orders WHERE CONVERT(INT, OrderNo) = @x;
            END
            GO
            CREATE PROCEDURE dbo.ProbeCastToWiderNumericCategory @x BIGINT AS
            BEGIN
                SELECT Code FROM dbo.Orders WHERE CAST(OrderNo AS BIGINT) = @x;
            END
            GO
            CREATE PROCEDURE dbo.ProbeNoOpCastOnString @x VARCHAR(20) AS
            BEGIN
                SELECT Code FROM dbo.Orders WHERE CAST(Code AS VARCHAR(20)) = @x;
            END
            GO
            CREATE PROCEDURE dbo.ProbeCastToNarrowerNumericInRange @x SMALLINT AS
            BEGIN
                SELECT Code FROM dbo.Orders WHERE CAST(OrderNo AS SMALLINT) = @x;
            END
            GO
            CREATE PROCEDURE dbo.ProbeCastDatetimeToDateTruncation @x DATE AS
            BEGIN
                SELECT Code FROM dbo.Orders WHERE CAST(PlacedAt AS DATE) = @x;
            END
            GO
            CREATE PROCEDURE dbo.ProbeCastNarrowingDecimalScale @x DECIMAL(5,0) AS
            BEGIN
                SELECT Code FROM dbo.Orders WHERE CAST(Amount AS DECIMAL(5,0)) = @x;
            END
            GO
            CREATE PROCEDURE dbo.ProbeBareColumn @x INT AS
            BEGIN
                SELECT Code FROM dbo.Orders WHERE OrderNo = @x;
            END
            GO
            """,
            DatabaseName);
    }

    public async Task DisposeAsync() =>
        await _provisioner.DropIfExistsAsync(DatabaseName);

    private async Task<bool> HasIndexSeek(string probe, string indexName)
    {
        var planXml = await new PlanXmlCapture(_options).CaptureAsync(DatabaseName, probe);
        return IndexAccessDetector.HasIndexSeek(planXml, indexName);
    }

    [Fact]
    public async Task CastChangesCategory_NeverSeeks() =>
        Assert.False(await HasIndexSeek("EXEC dbo.ProbeCastChangesCategory @x = '100';", "IX_Orders_OrderNo"));

    [Fact]
    public async Task NoOpConvertOnNumericColumn_StillSeeks() =>
        Assert.True(await HasIndexSeek("EXEC dbo.ProbeNoOpConvertOnNumeric @x = 100;", "IX_Orders_OrderNo"));

    [Fact]
    public async Task CastToWiderNumericCategory_StillSeeks() =>
        Assert.True(await HasIndexSeek("EXEC dbo.ProbeCastToWiderNumericCategory @x = 100;", "IX_Orders_OrderNo"));

    [Fact]
    public async Task NoOpCastOnIdenticalStringType_StillNeverSeeks() =>
        Assert.False(await HasIndexSeek("EXEC dbo.ProbeNoOpCastOnString @x = 'C10';", "IX_Orders_Code"));

    [Fact]
    public async Task BareColumnComparison_Seeks() =>
        Assert.True(await HasIndexSeek("EXEC dbo.ProbeBareColumn @x = 100;", "IX_Orders_OrderNo"));

    [Fact]
    public async Task CastToNarrowerNumericInRange_StillSeeks() =>
        Assert.True(await HasIndexSeek("EXEC dbo.ProbeCastToNarrowerNumericInRange @x = 100;", "IX_Orders_OrderNo"));

    [Fact]
    public async Task CastDatetimeToDateTruncation_NeverSeeks() =>
        Assert.False(await HasIndexSeek("EXEC dbo.ProbeCastDatetimeToDateTruncation @x = '2020-01-01';", "IX_Orders_PlacedAt"));

    [Fact]
    public async Task CastNarrowingDecimalScale_StillSeeks() =>
        Assert.True(await HasIndexSeek("EXEC dbo.ProbeCastNarrowingDecimalScale @x = 100;", "IX_Orders_Amount"));
}
