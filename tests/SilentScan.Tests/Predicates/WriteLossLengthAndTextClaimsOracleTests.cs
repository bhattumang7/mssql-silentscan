using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
public sealed class WriteLossLengthAndTextClaimsOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(WriteLossLengthAndTextClaimsOracleTests);

    protected override string Ddl => "CREATE TABLE dbo.Anchor (Id INT);";

    [Theory]
    [Trait("Rule", FindingRuleIds.WriteLossLengthTruncationRuleId)]
    [InlineData("VARCHAR(MAX)", "REPLICATE(CAST('a' AS VARCHAR(MAX)), 5000)", "VARCHAR(5)", "aaaaa")]
    [InlineData("VARBINARY(10)", "0x41424344454647484950", "VARCHAR(5)", "ABCDE")]
    [InlineData("VARBINARY(11)", "0x4100420043004400450046", "NVARCHAR(5)", "ABCDE")]
    [InlineData("DATETIME2(7)", "'2024-01-02 03:04:05.1234567'", "VARCHAR(10)", "2024-01-02")]
    [InlineData("DATE", "'2024-01-02'", "VARCHAR(7)", "2024-01")]
    [InlineData("TIME(0)", "'03:04:05'", "VARCHAR(5)", "03:04")]
    [InlineData("DATETIMEOFFSET(7)", "'2024-01-02 03:04:05.1234567 +05:30'", "VARCHAR(27)", "2024-01-02 03:04:05.1234567")]
    public async Task AVariableAssignmentCutsTheValueWithoutAnError(string sourceType, string value, string targetType, string kept)
    {
        var result = await ScalarAsync<string>($"DECLARE @s {sourceType} = {value}; DECLARE @t {targetType} = @s; SELECT @t;");

        Assert.Equal(kept, result);
    }

    [Theory]
    [Trait("Rule", FindingRuleIds.WriteLossApproximateTruncationRuleId)]
    [InlineData("FLOAT", "1.29e0", "DECIMAL(5,1)", "1.3")]
    [InlineData("FLOAT", "1.23456e0", "MONEY", "1.2346")]
    [InlineData("REAL", "2.567", "DECIMAL(5,1)", "2.6")]
    [InlineData("FLOAT", "19.99e0", "INT", "19")]
    public async Task ApproximateValuesRoundIntoDecimalAndMoneyAndTruncateIntoIntegers(string sourceType, string value, string targetType, string stored)
    {
        await AssertStoredAsync(sourceType, value, targetType, stored);
    }

    [Theory]
    [Trait("Rule", FindingRuleIds.WriteLossNumericScaleNarrowingRuleId)]
    [InlineData("DECIMAL(10,2)", "7.89", "DECIMAL(5,1)", "7.9")]
    [InlineData("DECIMAL(10,2)", "7.89", "INT", "7")]
    [InlineData("MONEY", "7.89", "TINYINT", "8")]
    [InlineData("MONEY", "7.89", "DECIMAL(5,1)", "7.9")]
    public async Task DecimalsRoundToASmallerScaleAndTruncateIntoIntegersWhileMoneyRounds(string sourceType, string value, string targetType, string stored)
    {
        await AssertStoredAsync(sourceType, value, targetType, stored);
    }

    [Fact]
    [Trait("Rule", FindingRuleIds.WriteLossTemporalScaleNarrowingRuleId)]
    public async Task RoundingToAShorterScaleCanChangeTheDate()
    {
        await AssertStoredAsync("DATETIME2(7)", "'2024-12-31 23:59:59.9999999'", "DATETIME2(0)", "2025-01-01T00:00:00");
    }

    private async Task AssertStoredAsync(string sourceType, string value, string targetType, string stored)
    {
        var result = await ScalarAsync<string>(
            $"DECLARE @s {sourceType} = {value}; DECLARE @t {targetType} = @s; SELECT CONVERT(VARCHAR(40), @t, 126);");

        Assert.Equal(stored, result);
    }
}
