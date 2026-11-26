using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.NotNullPredicateContradictionRuleId)]
public sealed class InsteadOfInsertedConstraintOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => "SilentScanInsteadOfInserted";

    protected override string Ddl => """
        CREATE TABLE dbo.Guarded (Qty INT NOT NULL CONSTRAINT CK_Guarded CHECK (Qty >= 0));
        CREATE TABLE dbo.Seen (Kind VARCHAR(10) NOT NULL);
        GO
        CREATE TRIGGER dbo.GuardedTrigger ON dbo.Guarded INSTEAD OF INSERT AS
        BEGIN
            INSERT INTO dbo.Seen (Kind) SELECT 'missing' FROM inserted WHERE Qty IS NULL;
            INSERT INTO dbo.Seen (Kind) SELECT 'negative' FROM inserted WHERE Qty < 0;
        END;
        GO
        """;

    [Fact]
    public async Task InsertedHoldsUncheckedRows_AndNeitherPredicateIsReported()
    {
        await ExecuteAsync("INSERT INTO dbo.Guarded (Qty) VALUES (NULL), (-1);");

        Assert.Equal(2, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Seen;"));

        var report = await EngineAuthoritativeScan.ScanAsync(Ddl);
        Assert.DoesNotContain(
            report.FindingsByRuleId.Values.SelectMany(findings => findings),
            f => f is CheckConstraintPredicateContradictionFinding or CartesianJoinFinding);
    }
}
