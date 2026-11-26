using SilentScan.Core.Predicates;
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
        CREATE TABLE dbo.EdW (Id INT NOT NULL PRIMARY KEY, WinCol VARCHAR(20) COLLATE Latin1_General_CI_AS NOT NULL, SqlCol VARCHAR(20) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL, Pad CHAR(100) NOT NULL DEFAULT 'x');
        INSERT INTO dbo.EdW (Id, WinCol, SqlCol)
        SELECT TOP (20000) ROW_NUMBER() OVER (ORDER BY (SELECT 1)), 'v' + CAST(ROW_NUMBER() OVER (ORDER BY (SELECT 1)) AS VARCHAR(10)), 'v' + CAST(ROW_NUMBER() OVER (ORDER BY (SELECT 1)) AS VARCHAR(10))
        FROM sys.all_objects a CROSS JOIN sys.all_objects b;
        CREATE INDEX IX_EdW_Win ON dbo.EdW (WinCol);
        CREATE INDEX IX_EdW_Sql ON dbo.EdW (SqlCol);
        GO
        CREATE TABLE dbo.EdN (Id INT NOT NULL PRIMARY KEY, N VARCHAR(20) NULL, Pad CHAR(100) NOT NULL DEFAULT 'x');
        INSERT INTO dbo.EdN (Id, N) SELECT Id, S FROM dbo.Ed;
        CREATE INDEX IX_EdN_N ON dbo.EdN (N);
        GO
        CREATE VIEW dbo.VNullSame AS SELECT Id, CAST(N AS VARCHAR(20)) AS X FROM dbo.EdN;
        GO
        CREATE VIEW dbo.VNullStyled AS SELECT Id, CONVERT(VARCHAR(20), N, 0) AS X FROM dbo.EdN;
        GO
        CREATE VIEW dbo.VWinMax AS SELECT Id, CAST(WinCol AS NVARCHAR(MAX)) AS X FROM dbo.EdW;
        GO
        CREATE VIEW dbo.VWinWide AS SELECT CAST(WinCol AS NVARCHAR(20)) AS X, Pad FROM dbo.EdW;
        GO
        CREATE VIEW dbo.VWinNarrow AS SELECT CAST(WinCol AS NVARCHAR(3)) AS X, Pad FROM dbo.EdW;
        GO
        CREATE VIEW dbo.VSqlWide AS SELECT CAST(SqlCol AS NVARCHAR(20)) AS X, Pad FROM dbo.EdW;
        GO
        """;

    private Task<string> PlanAsync(string statement) => new PlanXmlCapture(Options).CaptureAsync(DatabaseName, statement);

    [Theory]
    [InlineData("SELECT Pad FROM dbo.VBig WHERE IdBig = 5;", "PK__Ed")]
    [InlineData("SELECT Pad FROM dbo.VDate WHERE D = '2024-03-01';", "IX_Ed_Dt")]
    [InlineData("SELECT Pad FROM dbo.VWinWide WHERE X = N'v5';", "IX_EdW_Win")]
    public async Task NumericOrDateCastExposedByAView_StillSeeksTheBaseColumnIndex(string statement, string expectedIndex)
    {
        var plan = await PlanAsync(statement);

        Assert.Contains(expectedIndex, plan, StringComparison.Ordinal);
        Assert.Contains("<SeekPredicates>", plan, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SameTypeCastOfANullableColumnExposedByAView_SeeksTheBaseColumnIndex()
    {
        var plan = await PlanAsync("SELECT Id FROM dbo.VNullSame WHERE X = 'v5';");

        Assert.True(IndexAccessDetector.HasIndexSeek(plan, "IX_EdN_N"), plan);
    }

    [Theory]
    [InlineData("SELECT Id FROM dbo.VNullStyled WHERE X = 'v5';", "IX_EdN_N")]
    [InlineData("SELECT Id FROM dbo.VWinMax WHERE X = N'v5';", "IX_EdW_Win")]
    public async Task CastTheEngineCannotRemoveExposedByAView_NeverSeeksTheBaseColumnIndex(string statement, string index)
    {
        var plan = await PlanAsync(statement);

        Assert.False(IndexAccessDetector.HasIndexSeek(plan, index), plan);
    }

    [Theory]
    [InlineData("VARCHAR(20) NULL", "CAST(N AS VARCHAR(20))", false)]
    [InlineData("VARCHAR(20) NOT NULL", "CAST(N AS VARCHAR(20))", true)]
    [InlineData("VARCHAR(20) NULL", "CONVERT(VARCHAR(20), N, 0)", true)]
    [InlineData("VARCHAR(20) NULL", "CAST(N AS NVARCHAR(MAX))", true)]
    [InlineData("VARCHAR(20) NULL", "CAST(N AS VARCHAR(20)) COLLATE Latin1_General_CS_AS", true)]
    public async Task ScannerReportsAViewCastOnlyWhenTheEngineKeepsTheConversion(string columnType, string viewExpression, bool reported)
    {
        var sql = string.Join(
            "\nGO\n",
            $"CREATE TABLE dbo.EdScan (N {columnType}, INDEX IX_EdScan (N));",
            $"CREATE VIEW dbo.VEdScan AS SELECT {viewExpression} AS X FROM dbo.EdScan;",
            "SELECT 1 FROM dbo.VEdScan WHERE X = 'v5';");

        var report = await EngineAuthoritativeScan.ScanAsync(sql, "SQL_Latin1_General_CP1_CI_AS");

        Assert.Equal(reported, report.Find<ExpressionDerivedFinding>("TypedPredicateExtractor").Count != 0);
    }

    [Theory]
    [InlineData("SELECT Pad FROM dbo.VStr WHERE IdStr = '5';")]
    [InlineData("SELECT Pad FROM dbo.VUpper WHERE US = 'V5';")]
    [InlineData("SELECT Pad FROM dbo.VPlus WHERE IdPlus = 6;")]
    [InlineData("SELECT Pad FROM dbo.VWinNarrow WHERE X = N'v5';")]
    [InlineData("SELECT Pad FROM dbo.VSqlWide WHERE X = N'v5';")]
    public async Task StringCastOrExpressionExposedByAView_CannotSeekTheBaseColumnIndex(string statement)
    {
        var plan = await PlanAsync(statement);

        Assert.DoesNotContain("<SeekPredicates>", plan.Split("Lookup", 2)[0], StringComparison.Ordinal);
        Assert.Contains("Scan", plan, StringComparison.Ordinal);
    }
}
