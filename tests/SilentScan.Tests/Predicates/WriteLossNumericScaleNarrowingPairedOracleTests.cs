using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.WriteLossNumericScaleNarrowingRuleId)]
public sealed class WriteLossNumericScaleNarrowingPairedOracleTests : RuleOracle
{
    private const string Lossy = "CREATE PROCEDURE dbo.P AS BEGIN DECLARE @src DECIMAL(10,4) = 123.4567; DECLARE @v DECIMAL(5,2); SET @v = @src; SELECT CAST(@v AS DECIMAL(38, 4)) AS V; END\nGO\n";

    private const string Lossless = "CREATE PROCEDURE dbo.P AS BEGIN DECLARE @src DECIMAL(5,2) = 123.45; DECLARE @v DECIMAL(5,2); SET @v = @src; SELECT CAST(@v AS DECIMAL(38, 4)) AS V; END\nGO\n";

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
