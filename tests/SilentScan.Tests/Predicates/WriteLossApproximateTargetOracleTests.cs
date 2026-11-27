using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.WriteLossApproximateTruncationRuleId)]
public sealed class WriteLossApproximateTargetOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(WriteLossApproximateTargetOracleTests);

    protected override string Ddl => """
        CREATE TABLE dbo.Targets (DecCol DECIMAL(5,2) NULL, MoneyCol MONEY NULL, IntCol INT NULL, SmallCol SMALLINT NULL, TinyCol TINYINT NULL, BigCol BIGINT NULL, FloatCol FLOAT NULL);
        """;

    [Fact]
    public async Task FloatIntoDecimalAndMoney_RoundsAwayFromZero_FloatIntoIntegerTypes_TruncatesTowardZero()
    {
        await ExecuteAsync("""
            DECLARE @pos FLOAT = 19.996e0;
            DECLARE @neg FLOAT = -4.7e0;
            DECLARE @tiny FLOAT = 0.123456e0;
            INSERT INTO dbo.Targets (DecCol, MoneyCol, IntCol, SmallCol, TinyCol, BigCol) VALUES (@pos, @tiny, @pos, @neg, @pos, @neg);
            INSERT INTO dbo.Targets (DecCol) VALUES (-1.235e0);
            """);

        var rows = await RowsAsync("SELECT DecCol, MoneyCol, IntCol, SmallCol, TinyCol, BigCol FROM dbo.Targets ORDER BY IntCol DESC");

        var first = rows[0];
        Assert.Equal(20.00m, first[0]);
        Assert.Equal(0.1235m, first[1]);
        Assert.Equal(19, first[2]);
        Assert.Equal((short)-4, first[3]);
        Assert.Equal((byte)19, first[4]);
        Assert.Equal(-4L, first[5]);
        Assert.Equal(-1.24m, rows[1][0]);
    }
}
