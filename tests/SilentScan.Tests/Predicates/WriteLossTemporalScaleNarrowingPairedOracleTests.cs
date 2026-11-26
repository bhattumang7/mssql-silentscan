using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.WriteLossTemporalScaleNarrowingRuleId)]
public sealed class WriteLossTemporalScaleNarrowingPairedOracleTests : RuleOracle
{
    private const string Lossy = "CREATE PROCEDURE dbo.P AS BEGIN DECLARE @src DATETIME2(7) = '2024-01-01 10:00:00.1234567'; DECLARE @v DATETIME2(2); SET @v = @src; SELECT CONVERT(VARCHAR(27), CAST(@v AS DATETIME2(7)), 121) AS V; END\nGO\n";

    private const string Lossless = "CREATE PROCEDURE dbo.P AS BEGIN DECLARE @src DATETIME2(2) = '2024-01-01 10:00:00.12'; DECLARE @v DATETIME2(2); SET @v = @src; SELECT CONVERT(VARCHAR(27), CAST(@v AS DATETIME2(7)), 121) AS V; END\nGO\n";

    private const string MillisecondLossy = "CREATE PROCEDURE dbo.P AS BEGIN DECLARE @src DATETIME2(3) = '2024-01-01 10:00:00.001'; DECLARE @v DATETIME; SET @v = @src; SELECT CONVERT(VARCHAR(27), CAST(@v AS DATETIME2(7)), 121) AS V; END\nGO\n";

    private const string MillisecondLossless = "CREATE PROCEDURE dbo.P AS BEGIN DECLARE @src DATETIME2(2) = '2024-01-01 10:00:00.01'; DECLARE @v DATETIME; SET @v = @src; SELECT CONVERT(VARCHAR(27), CAST(@v AS DATETIME2(7)), 121) AS V; END\nGO\n";

    private const string DateTimeIntoSeconds = "CREATE PROCEDURE dbo.P AS BEGIN DECLARE @src DATETIME = '2024-01-01 10:00:00.003'; DECLARE @v DATETIME2(0); SET @v = @src; SELECT CONVERT(VARCHAR(27), CAST(@v AS DATETIME2(7)), 121) AS V; END\nGO\n";

    private const string DateTimeIntoFullScale = "CREATE PROCEDURE dbo.P AS BEGIN DECLARE @src DATETIME = '2024-01-01 10:00:00.003'; DECLARE @v DATETIME2(7); SET @v = @src; SELECT CONVERT(VARCHAR(27), CAST(@v AS DATETIME2(7)), 121) AS V; END\nGO\n";

    private const string Narrowed = "value-assigned-to-a-narrower-variable";

    protected override IReadOnlyList<Func<IFinding, IFinding>> ClaimMutations { get; } =
    [
        finding => ((WriteLossFinding)finding) with { ColumnName = "@other" },
        finding => ((WriteLossFinding)finding) with { SourcePath = "dbo.Other" },
    ];

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(Narrowed, Lossy, "EXEC dbo.P;"),
        new("datetime2-milliseconds-assigned-to-datetime", MillisecondLossy, "EXEC dbo.P;", Narrowed),
        new("datetime-assigned-to-whole-seconds", DateTimeIntoSeconds, "EXEC dbo.P;", Narrowed),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("value-fits-the-variable", Lossless, "EXEC dbo.P;"), Narrowed, OwnSetup: true),
        new(new("centiseconds-fit-datetime", MillisecondLossless, "EXEC dbo.P;", "value-fits-the-variable"), "datetime2-milliseconds-assigned-to-datetime", OwnSetup: true),
        new(new("datetime-fits-datetime2-full-scale", DateTimeIntoFullScale, "EXEC dbo.P;", "value-fits-the-variable"), "datetime-assigned-to-whole-seconds", OwnSetup: true),
    ];
}
