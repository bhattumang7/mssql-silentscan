using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.WriteLossSmallDateTimeRoundingRuleId)]
public sealed class SmallDateTimeRoundingOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(SmallDateTimeRoundingOracleTests);

    protected override string Ddl => "CREATE TABLE dbo.Slot (Id INT NOT NULL, At SMALLDATETIME NOT NULL);";

    private async Task<int> VariableAssignmentKeepsTheValueAsync(string sourceType, string literal) =>
        await ScalarAsync<int>(
            $"DECLARE @src {sourceType} = '{literal}'; DECLARE @dst SMALLDATETIME = @src; "
            + "SELECT CASE WHEN CAST(@dst AS DATETIME2(7)) = CAST(@src AS DATETIME2(7)) THEN 1 ELSE 0 END;");

    private async Task<int> ColumnInsertKeepsTheValueAsync(string sourceType, string literal) =>
        await ScalarAsync<int>(
            $"DECLARE @src {sourceType} = '{literal}'; DELETE dbo.Slot; INSERT dbo.Slot (Id, At) VALUES (1, @src); "
            + "SELECT CASE WHEN (SELECT CAST(At AS DATETIME2(7)) FROM dbo.Slot) = CAST(@src AS DATETIME2(7)) THEN 1 ELSE 0 END;");

    [Theory]
    [InlineData("DATETIME2(3)", "2024-01-01 10:20:45.500")]
    [InlineData("DATETIME2(0)", "2024-01-01 10:20:29")]
    [InlineData("DATETIME", "2024-01-01 10:20:30.000")]
    public async Task SourceCarryingSeconds_ChangesWhenAssignedToSmallDateTime(string sourceType, string literal)
    {
        Assert.Equal(0, await VariableAssignmentKeepsTheValueAsync(sourceType, literal));
        Assert.Equal(0, await ColumnInsertKeepsTheValueAsync(sourceType, literal));
    }

    [Theory]
    [InlineData("DATETIME2(3)", "2024-01-01 10:20:00.000")]
    [InlineData("DATETIME", "2024-01-01 00:00:00.000")]
    [InlineData("SMALLDATETIME", "2024-01-01 10:20:00")]
    public async Task MinuteAlignedSource_KeepsItsValueWhenAssignedToSmallDateTime(string sourceType, string literal)
    {
        Assert.Equal(1, await VariableAssignmentKeepsTheValueAsync(sourceType, literal));
        Assert.Equal(1, await ColumnInsertKeepsTheValueAsync(sourceType, literal));
    }
}
