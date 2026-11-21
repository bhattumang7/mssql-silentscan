using SilentScan.Tests.Support;
using SilentScan.Verify.Oracle;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.ExpressionDerivedRuleId)]
public sealed class ExpressionDerivedSeekableCastOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(ExpressionDerivedSeekableCastOracleTests);

    protected override string Ddl => """
        CREATE TABLE dbo.Ed (Id INT NOT NULL PRIMARY KEY, Dt DATETIME NOT NULL, S VARCHAR(20) NOT NULL, Pad CHAR(100) NOT NULL DEFAULT 'x');
        INSERT INTO dbo.Ed (Id, Dt, S)
        SELECT TOP (20000) ROW_NUMBER() OVER (ORDER BY (SELECT 1)),
               DATEADD(day, ROW_NUMBER() OVER (ORDER BY (SELECT 1)) % 500, '2024-01-01'),
               'v' + CAST(ROW_NUMBER() OVER (ORDER BY (SELECT 1)) AS VARCHAR(10))
        FROM sys.all_objects a CROSS JOIN sys.all_objects b;
        CREATE INDEX IX_Ed_Dt ON dbo.Ed (Dt);
        CREATE INDEX IX_Ed_S ON dbo.Ed (S);
        GO
        CREATE VIEW dbo.VBig AS SELECT CAST(Id AS BIGINT) AS IdBig, Pad FROM dbo.Ed;
        GO
        CREATE VIEW dbo.VDate AS SELECT CAST(Dt AS DATE) AS D, Pad FROM dbo.Ed;
        GO
        CREATE VIEW dbo.VStr AS SELECT CAST(Id AS VARCHAR(20)) AS IdStr, Pad FROM dbo.Ed;
        GO
        CREATE VIEW dbo.VUpper AS SELECT UPPER(S) AS US, Pad FROM dbo.Ed;
        GO
        CREATE VIEW dbo.VPlus AS SELECT Id + 1 AS IdPlus, Pad FROM dbo.Ed;
        GO
        """;

    private Task<string> PlanAsync(string statement) => new PlanXmlCapture(Options).CaptureAsync(DatabaseName, statement);

    [Theory]
    [InlineData("SELECT Pad FROM dbo.VBig WHERE IdBig = 5;", "PK__Ed")]
    [InlineData("SELECT Pad FROM dbo.VDate WHERE D = '2024-03-01';", "IX_Ed_Dt")]
    public async Task NumericOrDateCastExposedByAView_StillSeeksTheBaseColumnIndex(string statement, string expectedIndex)
    {
        var plan = await PlanAsync(statement);

        Assert.Contains(expectedIndex, plan, StringComparison.Ordinal);
        Assert.Contains("<SeekPredicates>", plan, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("SELECT Pad FROM dbo.VStr WHERE IdStr = '5';")]
    [InlineData("SELECT Pad FROM dbo.VUpper WHERE US = 'V5';")]
    [InlineData("SELECT Pad FROM dbo.VPlus WHERE IdPlus = 6;")]
    public async Task StringCastOrExpressionExposedByAView_CannotSeekTheBaseColumnIndex(string statement)
    {
        var plan = await PlanAsync(statement);

        Assert.DoesNotContain("<SeekPredicates>", plan.Split("Lookup", 2)[0], StringComparison.Ordinal);
        Assert.Contains("Scan", plan, StringComparison.Ordinal);
    }
}
