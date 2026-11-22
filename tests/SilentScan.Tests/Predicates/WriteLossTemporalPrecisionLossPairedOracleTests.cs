using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.WriteLossTemporalPrecisionLossRuleId)]
public sealed class WriteLossTemporalPrecisionLossPairedOracleTests : RuleOracle
{
    private const string Lossy = "CREATE PROCEDURE dbo.P AS BEGIN DECLARE @src DATETIME2(0) = '2024-01-02 10:30:00'; DECLARE @v DATE; SET @v = @src; SELECT @v AS V; END\nGO\n";

    private const string Lossless = "CREATE PROCEDURE dbo.P AS BEGIN DECLARE @src DATE = '2024-01-02'; DECLARE @v DATE; SET @v = @src; SELECT @v AS V; END\nGO\n";

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
