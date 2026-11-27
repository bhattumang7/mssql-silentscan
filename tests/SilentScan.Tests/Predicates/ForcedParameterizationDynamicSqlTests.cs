using SilentScan.Core.Predicates;
using SilentScan.Core.Reporting;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

public sealed class ForcedParameterizationDynamicSqlTests
{
    private const string Forced = "ALTER DATABASE CURRENT SET PARAMETERIZATION FORCED;";

    private const string Tables = """
        CREATE TABLE dbo.T (Id INT NOT NULL PRIMARY KEY, Name VARCHAR(40) NOT NULL, Val INT NOT NULL, D DATETIME NOT NULL);
        """;

    private static Task<ScanReport> ScanAsync(string setting, string inner) =>
        EngineAuthoritativeScan.ScanAsync($"""
            {setting}
            GO
            {Tables}
            GO
            CREATE PROCEDURE dbo.P @s VARCHAR(20), @n INT AS
            BEGIN
                DECLARE @fixed VARCHAR(20) = 'abc';
                DECLARE @sql NVARCHAR(MAX) = {inner};
                EXEC (@sql);
            END;
            """);

    private static List<ForcedParameterizationFinding> Forcing(ScanReport report) =>
        report.Find<ForcedParameterizationFinding>(nameof(ForcedParameterizationScanner)).ToList();

    private static List<UnparameterizedDynamicSqlFinding> Concatenated(ScanReport report) =>
        report.FindingsByRuleId.Values.SelectMany(f => f).OfType<UnparameterizedDynamicSqlFinding>().ToList();

    [Theory]
    [InlineData("N'SELECT Id FROM dbo.T WHERE Name LIKE ''x' + @s + N'%'''", ForcedParameterizationFindingKind.LikePatternLiteral)]
    [InlineData("N'SELECT TOP (' + CAST(@n AS NVARCHAR(10)) + N') Id FROM dbo.T'", ForcedParameterizationFindingKind.TopOrPagingLiteral)]
    [InlineData("N'SELECT Id FROM dbo.T ORDER BY Id OFFSET ' + CAST(@n AS NVARCHAR(10)) + N' ROWS'", ForcedParameterizationFindingKind.TopOrPagingLiteral)]
    [InlineData("N'SELECT ''' + @s + N''' AS Tag, Id FROM dbo.T'", ForcedParameterizationFindingKind.SelectListLiteral)]
    [InlineData("N'SELECT Id FROM dbo.T ORDER BY CHARINDEX(''' + @s + N''', Name)'", ForcedParameterizationFindingKind.OrderByExpressionLiteral)]
    [InlineData("N'SELECT COUNT(*) FROM dbo.T GROUP BY CHARINDEX(''' + @s + N''', Name)'", ForcedParameterizationFindingKind.GroupByExpressionLiteral)]
    [InlineData("N'SELECT Val FROM dbo.T GROUP BY Val HAVING MAX(Name) > ''' + @s + N''''", ForcedParameterizationFindingKind.HavingLiteral)]
    [InlineData("N'UPDATE dbo.T SET Val = 1 OUTPUT ''' + @s + N''' AS Tag'", ForcedParameterizationFindingKind.DmlOutputListLiteral)]
    [InlineData("N'SELECT CONVERT(VARCHAR(30), D, ' + CAST(@n AS NVARCHAR(10)) + N') FROM dbo.T'", ForcedParameterizationFindingKind.ConvertStyleCodeLiteral)]
    [InlineData("N'SELECT Id FROM dbo.T TABLESAMPLE (' + CAST(@n AS NVARCHAR(10)) + N' ROWS)'", ForcedParameterizationFindingKind.TableSampleSizeLiteral)]
    public async Task ChangingValueInASkippedPosition_FiresThatKind(string inner, ForcedParameterizationFindingKind expected)
    {
        var report = await ScanAsync(Forced, inner);

        Assert.Equal(expected, Assert.Single(Forcing(report)).Kind);
        Assert.Empty(Concatenated(report));
    }

    [Theory]
    [InlineData(Forced, "N'SELECT a.Id FROM dbo.T AS a JOIN dbo.T AS b ON b.Id = a.Id WHERE a.Name = ''' + @s + N''''")]
    [InlineData(Forced, "N'SELECT Id FROM dbo.T WHERE Name LIKE ''x' + @fixed + N'%'''")]
    [InlineData(Forced, "N'SELECT Id FROM dbo.T WHERE Name LIKE ''abc%'''")]
    [InlineData(Forced, "N'SELECT Id FROM dbo.T WHERE Name LIKE ''x' + @s + N'%'' OPTION (RECOMPILE)'")]
    [InlineData(Forced, "N'SELECT ' + @s + N' FROM dbo.T'")]
    [InlineData("", "N'SELECT Id FROM dbo.T WHERE Name LIKE ''x' + @s + N'%'''")]
    public async Task NothingThatAddsAPlanPerValue_FiresNoForcedFinding(string setting, string inner) =>
        Assert.Empty(Forcing(await ScanAsync(setting, inner)));

    [Fact]
    public async Task ForcedDatabaseValueTheEngineParameterizes_IsNotReportedAsConcatenated() =>
        Assert.Empty(Concatenated(await ScanAsync(
            Forced, "N'SELECT a.Id FROM dbo.T AS a JOIN dbo.T AS b ON b.Id = a.Id WHERE a.Name = ''' + @s + N''''")));

    [Fact]
    public async Task ForcedDatabaseStatementWithALocalVariable_IsReportedByTheGeneralRule()
    {
        var report = await ScanAsync(
            Forced, "N'DECLARE @v INT = 1; SELECT Id FROM dbo.T WHERE Val = @v AND Name = ''' + @s + N''''");

        Assert.Contains(Concatenated(report), f => f.Kind == UnparameterizedDynamicSqlFindingKind.ConcatenatedValueInConstantSql);
        Assert.Empty(Forcing(report));
    }

    [Fact]
    public async Task LiteralInAModuleStatement_NeverFires()
    {
        var report = await EngineAuthoritativeScan.ScanAsync($"""
            {Forced}
            GO
            {Tables}
            GO
            CREATE PROCEDURE dbo.Q AS SELECT TOP (7) Id FROM dbo.T WHERE Name LIKE 'abc%';
            """);

        Assert.Empty(Forcing(report));
    }
}
