using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.SetOptionQuotedIdentifierOffBlocksIndexedFeatureRuleId)]
[Trait("Rule", FindingRuleIds.SetOptionAnsiNullsOffBlocksIndexedFeatureRuleId)]
[Trait("Rule", FindingRuleIds.SetOptionAnsiPaddingOffBlocksIndexedFeatureRuleId)]
[Trait("Rule", FindingRuleIds.SetOptionAnsiWarningsOffBlocksIndexedFeatureRuleId)]
[Trait("Rule", FindingRuleIds.SetOptionConcatNullYieldsNullOffBlocksIndexedFeatureRuleId)]
[Trait("Rule", FindingRuleIds.SetOptionNumericRoundabortOnBlocksIndexedFeatureRuleId)]
public sealed class SetOptionWriteTargetEngineFactOracleTests : OracleTestFixture
{
    private const int WrongSetOptionsForTheStatement = 1934;

    protected override string DatabaseNameSeed => "SilentScanSetOptionWrites";

    protected override string Ddl => """
        CREATE TABLE dbo.Filtered (Id INT NOT NULL, A INT NOT NULL);
        CREATE TABLE dbo.Other (Id INT NOT NULL);
        GO
        INSERT INTO dbo.Filtered (Id, A) VALUES (1, 1);
        INSERT INTO dbo.Other (Id) VALUES (1);
        GO
        CREATE INDEX IX_Filtered ON dbo.Filtered (Id) WHERE A = 1;
        GO
        """;

    [Theory]
    [InlineData("SET QUOTED_IDENTIFIER OFF")]
    [InlineData("SET ANSI_NULLS OFF")]
    [InlineData("SET ANSI_PADDING OFF")]
    [InlineData("SET ANSI_WARNINGS OFF")]
    [InlineData("SET CONCAT_NULL_YIELDS_NULL OFF")]
    [InlineData("SET NUMERIC_ROUNDABORT ON")]
    public async Task OnlyAReadSucceedsSilently_AWriteToTheIndexedTableRaises(string option)
    {
        await using var connection = await OpenConnectionAsync();
        await ExecuteAsync(connection, option + ";");

        Assert.Null(await SqlErrorNumberAsync(connection, "SELECT Id FROM dbo.Filtered WHERE A = 1;"));
        Assert.Null(await SqlErrorNumberAsync(connection, "UPDATE dbo.Other SET Id = f.Id FROM dbo.Other AS o JOIN dbo.Filtered AS f ON f.Id = o.Id WHERE f.A = 1;"));
        Assert.Equal(WrongSetOptionsForTheStatement, await SqlErrorNumberAsync(connection, "UPDATE dbo.Filtered SET Id = Id WHERE A = 1;"));
        Assert.Equal(WrongSetOptionsForTheStatement, await SqlErrorNumberAsync(connection, "DELETE dbo.Filtered WHERE A = 1;"));
        Assert.Equal(WrongSetOptionsForTheStatement, await SqlErrorNumberAsync(connection, "INSERT INTO dbo.Filtered (Id, A) VALUES (2, 1);"));
    }
}
