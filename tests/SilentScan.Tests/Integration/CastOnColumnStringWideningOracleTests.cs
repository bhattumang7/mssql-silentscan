using SilentScan.Tests.Support;
using SilentScan.Verify.Oracle;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.Tier1CastOrConvertOnColumnRuleId)]
public sealed class CastOnColumnStringWideningOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(CastOnColumnStringWideningOracleTests);

    protected override string Ddl => """
        CREATE TABLE dbo.Names
        (
            Id INT NOT NULL PRIMARY KEY,
            WinCol VARCHAR(20) COLLATE Latin1_General_CI_AS NOT NULL,
            SqlCol VARCHAR(20) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
            Pad CHAR(100) NOT NULL DEFAULT 'x'
        );
        GO
        INSERT INTO dbo.Names (Id, WinCol, SqlCol)
        SELECT TOP (20000) ROW_NUMBER() OVER (ORDER BY (SELECT 1)),
               'v' + CAST(ROW_NUMBER() OVER (ORDER BY (SELECT 1)) AS VARCHAR(10)),
               'v' + CAST(ROW_NUMBER() OVER (ORDER BY (SELECT 1)) AS VARCHAR(10))
        FROM sys.all_objects a CROSS JOIN sys.all_objects b;
        CREATE INDEX IX_Names_Win ON dbo.Names (WinCol);
        CREATE INDEX IX_Names_Sql ON dbo.Names (SqlCol);
        GO
        """;

    private Task<string> PlanAsync(string statement) => new PlanXmlCapture(Options).CaptureAsync(DatabaseName, statement);

    [Fact]
    public async Task WindowsCollationVarcharCastToAnAtLeastAsWideNvarchar_StillSeeksTheColumnIndex()
    {
        var plan = await PlanAsync("SELECT Pad FROM dbo.Names WHERE CAST(WinCol AS NVARCHAR(20)) = N'v5';");

        Assert.Contains("IX_Names_Win", plan, StringComparison.Ordinal);
        Assert.Contains("<SeekPredicates>", plan, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("SELECT Pad FROM dbo.Names WHERE CAST(SqlCol AS NVARCHAR(20)) = N'v5';")]
    [InlineData("SELECT Pad FROM dbo.Names WHERE CAST(WinCol AS NVARCHAR(3)) = N'v5';")]
    public async Task SqlCollationOrNarrowerCast_ScansInsteadOfSeekingTheColumnIndex(string statement)
    {
        var plan = await PlanAsync(statement);

        Assert.DoesNotContain("<SeekPredicates>", plan.Split("Lookup", 2)[0], StringComparison.Ordinal);
        Assert.Contains("Scan", plan, StringComparison.Ordinal);
    }
}
