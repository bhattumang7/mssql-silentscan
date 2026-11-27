using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.UnderLengthParameterRuleId)]
public sealed class UnderLengthColumnSourceEngineFactOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => "SilentScanUnderLengthSource";

    protected override string Ddl => """
        CREATE TABLE dbo.Customers (Code VARCHAR(20) NOT NULL);
        CREATE TABLE dbo.Codes (Country VARCHAR(2) NOT NULL);
        GO
        INSERT INTO dbo.Customers (Code) VALUES ('AB'), ('ABCDEF');
        INSERT INTO dbo.Codes (Country) VALUES ('AB');
        GO
        """;

    [Theory]
    [InlineData("SELECT @p = Country FROM dbo.Codes;", "AB")]
    [InlineData("SELECT @p = Code FROM dbo.Customers WHERE Code = 'ABCDEF';", "AB")]
    public async Task OnlyAWiderSourceLosesCharacters(string assignment, string held)
    {
        Assert.Equal(held, await ScalarAsync<string>($"DECLARE @p VARCHAR(2); {assignment} SELECT @p;"));
        Assert.Equal(
            1,
            await ScalarAsync<int>($"DECLARE @p VARCHAR(2); {assignment} SELECT COUNT(*) FROM dbo.Customers WHERE Code = @p;"));
    }
}
