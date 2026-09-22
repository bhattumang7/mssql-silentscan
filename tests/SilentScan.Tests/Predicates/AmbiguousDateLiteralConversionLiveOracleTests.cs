using Microsoft.Data.SqlClient;
using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;
using SilentScan.Verify;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", "silentscan/session-date/ambiguous-date-literal-conversion")]
public sealed class AmbiguousDateLiteralConversionLiveOracleTests
{
    [Fact]
    public async Task LiveDeployment_AmbiguousLiteralCastToDate_Fires()
    {
        var report = await EngineAuthoritativeScan.ScanAsync(
            """
            SELECT CAST('03/04/2026' AS date);
            """,
            minimumConfidence: FindingConfidence.Low);

        var finding = Assert.Single(report.Find<AmbiguousDateLiteralConversionFinding>("AmbiguousDateLiteralConversionScanner"));
        Assert.Equal("03/04/2026", finding.LiteralText);
    }

    [Fact]
    public async Task LiveDeployment_ConvertWithExplicitStyle_NeverFires()
    {
        var report = await EngineAuthoritativeScan.ScanAsync(
            """
            SELECT CONVERT(date, '03/04/2026', 103);
            """,
            minimumConfidence: FindingConfidence.Low);

        Assert.Empty(report.Find<AmbiguousDateLiteralConversionFinding>("AmbiguousDateLiteralConversionScanner"));
    }

    [Fact]
    public async Task LiveDeployment_IsoFormatLiteral_NeverFires()
    {
        var report = await EngineAuthoritativeScan.ScanAsync(
            """
            SELECT CAST('20260304' AS date);
            """,
            minimumConfidence: FindingConfidence.Low);

        Assert.Empty(report.Find<AmbiguousDateLiteralConversionFinding>("AmbiguousDateLiteralConversionScanner"));
    }

    [Fact]
    public async Task LiveDeployment_YearFirstAmbiguousLiteral_CastToDatetime_FiresBecauseDateformatSwapsMonthAndDay()
    {
        var report = await EngineAuthoritativeScan.ScanAsync(
            """
            SELECT CAST('2026-04-03' AS datetime);
            """,
            minimumConfidence: FindingConfidence.Low);

        var finding = Assert.Single(report.Find<AmbiguousDateLiteralConversionFinding>("AmbiguousDateLiteralConversionScanner"));
        Assert.Equal("2026-04-03", finding.LiteralText);
    }

    [Fact]
    public async Task LiveDeployment_YearFirstAmbiguousLiteral_CastToDate_NeverFiresBecauseIsoFormatIsAlwaysUnambiguousForDate()
    {
        var report = await EngineAuthoritativeScan.ScanAsync(
            """
            SELECT CAST('2026-04-03' AS date);
            """,
            minimumConfidence: FindingConfidence.Low);

        Assert.Empty(report.Find<AmbiguousDateLiteralConversionFinding>("AmbiguousDateLiteralConversionScanner"));
    }

    [Fact]
    public async Task RealSession_SameSlashLiteralCastToDate_ResolvesToADifferentRealDateUnderMdyVsDmyDateformat()
    {
        await using var connection = new SqlConnection(SqlServerOptions.LocalDocker.BuildConnectionString());
        await connection.OpenAsync();

        await using var mdyCommand = connection.CreateCommand();
        mdyCommand.CommandText = "SET DATEFORMAT mdy; SELECT CAST('03/04/2026' AS date);";
        var mdyResult = (DateTime)(await mdyCommand.ExecuteScalarAsync())!;

        await using var dmyCommand = connection.CreateCommand();
        dmyCommand.CommandText = "SET DATEFORMAT dmy; SELECT CAST('03/04/2026' AS date);";
        var dmyResult = (DateTime)(await dmyCommand.ExecuteScalarAsync())!;

        Assert.Equal(new DateTime(2026, 3, 4), mdyResult);
        Assert.Equal(new DateTime(2026, 4, 3), dmyResult);
        Assert.NotEqual(mdyResult, dmyResult);
    }

    [Fact]
    public async Task RealSession_YearFirstLiteralCastToDatetime_ResolvesToADifferentRealDateUnderMdyVsDmyDateformat_ButCastToDateDoesNot()
    {
        await using var connection = new SqlConnection(SqlServerOptions.LocalDocker.BuildConnectionString());
        await connection.OpenAsync();

        await using var datetimeMdyCommand = connection.CreateCommand();
        datetimeMdyCommand.CommandText = "SET DATEFORMAT mdy; SELECT CAST('2026-04-03' AS datetime);";
        var datetimeMdyResult = (DateTime)(await datetimeMdyCommand.ExecuteScalarAsync())!;

        await using var datetimeDmyCommand = connection.CreateCommand();
        datetimeDmyCommand.CommandText = "SET DATEFORMAT dmy; SELECT CAST('2026-04-03' AS datetime);";
        var datetimeDmyResult = (DateTime)(await datetimeDmyCommand.ExecuteScalarAsync())!;

        Assert.NotEqual(datetimeMdyResult, datetimeDmyResult);

        await using var dateMdyCommand = connection.CreateCommand();
        dateMdyCommand.CommandText = "SET DATEFORMAT mdy; SELECT CAST('2026-04-03' AS date);";
        var dateMdyResult = (DateTime)(await dateMdyCommand.ExecuteScalarAsync())!;

        await using var dateDmyCommand = connection.CreateCommand();
        dateDmyCommand.CommandText = "SET DATEFORMAT dmy; SELECT CAST('2026-04-03' AS date);";
        var dateDmyResult = (DateTime)(await dateDmyCommand.ExecuteScalarAsync())!;

        Assert.Equal(dateMdyResult, dateDmyResult);
        Assert.Equal(new DateTime(2026, 4, 3), dateMdyResult);
    }
}
