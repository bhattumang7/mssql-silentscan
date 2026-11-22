using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.WriteLossTemporalScaleNarrowingRuleId)]
public sealed class WriteLossTemporalScaleNarrowingPairedOracleTests : RuleOracle
{
    private const string Lossy = "CREATE PROCEDURE dbo.P AS BEGIN DECLARE @src DATETIME2(7) = '2024-01-01 10:00:00.1234567'; DECLARE @v DATETIME2(2); SET @v = @src; SELECT CONVERT(VARCHAR(27), CAST(@v AS DATETIME2(7)), 121) AS V; END\nGO\n";

    private const string Lossless = "CREATE PROCEDURE dbo.P AS BEGIN DECLARE @src DATETIME2(2) = '2024-01-01 10:00:00.12'; DECLARE @v DATETIME2(2); SET @v = @src; SELECT CONVERT(VARCHAR(27), CAST(@v AS DATETIME2(7)), 121) AS V; END\nGO\n";

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
