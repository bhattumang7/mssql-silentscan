using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.WriteLossTemporalOffsetDroppedRuleId)]
public sealed class WriteLossTemporalOffsetDroppedPairedOracleTests : RuleOracle
{
    private const string Lossy = "CREATE PROCEDURE dbo.P AS BEGIN DECLARE @src DATETIMEOFFSET(0) = '2024-01-01 10:00:00 +05:30'; DECLARE @v DATETIME2(0); SET @v = @src; SELECT CONVERT(VARCHAR(40), @v, 121) AS V; END\nGO\n";

    private const string Lossless = "CREATE PROCEDURE dbo.P AS BEGIN DECLARE @src DATETIMEOFFSET(7) = '2024-01-01 10:00:00 +05:30'; DECLARE @v DATETIMEOFFSET(7); SET @v = @src; SELECT CONVERT(VARCHAR(40), @v, 121) AS V; END\nGO\n";

    private const string Narrowed = "value-assigned-to-a-narrower-variable";

    protected override IReadOnlyList<Func<IFinding, IFinding>> ClaimMutations { get; } =
    [
        finding => ((WriteLossFinding)finding) with { ColumnName = "@other" },
        finding => ((WriteLossFinding)finding) with { SourcePath = "dbo.Other" },
    ];

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(Narrowed, Lossy, "EXEC dbo.P;"),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("value-fits-the-variable", Lossless, "EXEC dbo.P;"), Narrowed, OwnSetup: true),
    ];
}
