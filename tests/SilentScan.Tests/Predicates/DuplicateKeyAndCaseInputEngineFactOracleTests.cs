using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.QueryAntiPatternMultiRowInsertIgnoreDupKeyDropRuleId)]
[Trait("Rule", FindingRuleIds.IndexDesignDuplicateIndexRuleId)]
[Trait("Rule", FindingRuleIds.ControlFlowRiskNonDeterministicCaseInputRuleId)]
[Trait("Rule", FindingRuleIds.PartialCompositeForeignKeyJoinRuleId)]
public sealed class DuplicateKeyAndCaseInputEngineFactOracleTests : OracleTestFixture
{
    private const int DuplicateKeyInUniqueIndex = 2601;

    protected override string DatabaseNameSeed => "SilentScanDuplicateKeyFacts";

    protected override string Ddl => """
        CREATE TABLE dbo.Codes (Id INT NOT NULL, Code INT NOT NULL);
        CREATE UNIQUE INDEX UX_Ignoring ON dbo.Codes (Code) WITH (IGNORE_DUP_KEY = ON);
        CREATE UNIQUE INDEX UX_Raising ON dbo.Codes (Code);
        GO
        INSERT INTO dbo.Codes (Id, Code) VALUES (1, 10), (2, 20);
        GO
        """;

    [Fact]
    public async Task EveryDuplicateInsertIsSkippedWhileUpdateAndMergeRaise()
    {
        Assert.Null(await SqlErrorNumberAsync("INSERT INTO dbo.Codes (Id, Code) VALUES (3, 10);"));
        Assert.Equal(2, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Codes;"));
        Assert.Equal(DuplicateKeyInUniqueIndex, await SqlErrorNumberAsync("UPDATE dbo.Codes SET Code = 10 WHERE Id = 2;"));
        Assert.Equal(DuplicateKeyInUniqueIndex, await SqlErrorNumberAsync(
            "MERGE dbo.Codes AS t USING (VALUES (3, 10)) AS s (Id, Code) ON t.Id = s.Id WHEN NOT MATCHED THEN INSERT (Id, Code) VALUES (s.Id, s.Code);"));
    }

    [Theory]
    [InlineData("UX_Raising", null)]
    [InlineData("UX_Ignoring", DuplicateKeyInUniqueIndex)]
    public async Task OnlyDroppingTheRaisingDuplicateKeepsInsertsBehaving(string dropped, int? errorAfterwards)
    {
        await ExecuteAsync($"DROP INDEX {dropped} ON dbo.Codes;");

        Assert.Equal(errorAfterwards, await SqlErrorNumberAsync("INSERT INTO dbo.Codes (Id, Code) VALUES (3, 10);"));
    }

    [Fact]
    public async Task AWrappedRandomCaseInputReachesBranchesAndStillFallsThrough()
    {
        var rows = await RowsAsync("""
            SELECT CASE ABS(CHECKSUM(NEWID())) % 3 WHEN 0 THEN 1 WHEN 1 THEN 1 WHEN 2 THEN 1 END
            FROM (SELECT TOP (3000) a.object_id FROM sys.all_columns AS a CROSS JOIN sys.all_columns AS b) AS numbers;
            """);

        Assert.Contains(rows, row => row[0] is null);
        Assert.Contains(rows, row => row[0] is not null);
    }

    [Fact]
    public async Task EqualityThroughAThirdColumnReturnsTheSameRowsAsTheDirectPredicate()
    {
        await ExecuteAsync("""
            CREATE TABLE dbo.P (A INT NOT NULL, B INT NOT NULL);
            CREATE TABLE dbo.C (A INT NOT NULL, B INT NOT NULL);
            CREATE TABLE dbo.R (B INT NOT NULL);
            INSERT INTO dbo.P VALUES (1, 1), (1, 2);
            INSERT INTO dbo.C VALUES (1, 1);
            INSERT INTO dbo.R VALUES (1), (2);
            """);

        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.C AS c JOIN dbo.R AS r ON r.B = c.B JOIN dbo.P AS p ON p.A = c.A AND p.B = r.B;"));
    }
}
