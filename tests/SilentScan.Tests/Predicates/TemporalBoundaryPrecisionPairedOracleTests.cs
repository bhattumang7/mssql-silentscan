using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.TemporalBoundaryPrecisionRuleId)]
public sealed class TemporalBoundaryPrecisionPairedOracleTests : RuleOracle
{
    private const string Setup = """
        CREATE TABLE dbo.Events (EventId INT NOT NULL PRIMARY KEY, OccurredAt DATETIME2(7) NOT NULL, Other DATETIME2(7) NOT NULL);
        GO
        CREATE TABLE dbo.Slots (SlotId INT NOT NULL PRIMARY KEY, StartsAt TIME(7) NOT NULL);
        GO
        CREATE TABLE dbo.Zoned (ZoneId INT NOT NULL PRIMARY KEY, SeenAt DATETIMEOFFSET(7) NOT NULL);
        GO
        INSERT INTO dbo.Events (EventId, OccurredAt, Other) VALUES (1, '2024-06-01', '2024-06-01');
        INSERT INTO dbo.Slots (SlotId, StartsAt) VALUES (1, '12:00:00');
        INSERT INTO dbo.Zoned (ZoneId, SeenAt) VALUES (1, '2024-06-01 00:00:00 +00:00');
        GO
        """;

    private const string DateTime2Between = "datetime2-between-short-literal";

    private const string TimeBetween = "time-between-short-literal";

    private const string OffsetBetween = "datetimeoffset-between-short-literal";

    private static IFinding NoGap(IFinding finding) =>
        ((TemporalBoundaryPrecisionFinding)finding) with { ColumnScale = ((TemporalBoundaryPrecisionFinding)finding).BoundaryLiteralFractionalDigits };

    private static IFinding BeforeTheRows(IFinding finding) =>
        ((TemporalBoundaryPrecisionFinding)finding) with { BoundaryLiteralText = "1999-01-01" };

    private static IFinding OtherColumn(IFinding finding) =>
        ((TemporalBoundaryPrecisionFinding)finding) with { ColumnName = "Other" };

    protected override IReadOnlyList<Func<IFinding, IFinding>> ClaimMutations { get; } = [NoGap, BeforeTheRows, OtherColumn];

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(DateTime2Between, Setup, "SELECT EventId FROM dbo.Events WHERE OccurredAt BETWEEN '2024-01-01' AND '2024-12-31 23:59:59.997';"),
        new(TimeBetween, Setup, "SELECT SlotId FROM dbo.Slots WHERE StartsAt BETWEEN '00:00:00' AND '23:59:59.997';"),
        new(OffsetBetween, Setup, "SELECT ZoneId FROM dbo.Zoned WHERE SeenAt BETWEEN '2024-01-01' AND '2024-12-31 23:59:59.997';"),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("half-open-range", Setup, "SELECT EventId FROM dbo.Events WHERE OccurredAt >= '2024-01-01' AND OccurredAt < '2025-01-01';"), DateTime2Between),
        new(new("time-half-open-range", Setup, "SELECT SlotId FROM dbo.Slots WHERE StartsAt >= '00:00:00' AND StartsAt < '23:59:59.998';", "half-open-range"), TimeBetween),
        new(new("offset-half-open-range", Setup, "SELECT ZoneId FROM dbo.Zoned WHERE SeenAt >= '2024-01-01' AND SeenAt < '2025-01-01';", "half-open-range"), OffsetBetween),
    ];
}
