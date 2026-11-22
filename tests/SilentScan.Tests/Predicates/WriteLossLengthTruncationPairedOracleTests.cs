using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.WriteLossLengthTruncationRuleId)]
public sealed class WriteLossLengthTruncationPairedOracleTests : RuleOracle
{
    private const string Truncated = "CREATE PROCEDURE dbo.P AS BEGIN DECLARE @src VARCHAR(20) = 'HelloWorldHello'; DECLARE @v VARCHAR(5); SET @v = @src; SELECT @v AS V; END\nGO\n";

    private const string Fitting = "CREATE PROCEDURE dbo.P AS BEGIN DECLARE @src VARCHAR(5) = 'Hello'; DECLARE @v VARCHAR(5); SET @v = @src; SELECT @v AS V; END\nGO\n";

    private const string Truncating = "value-assigned-to-a-shorter-variable";

    protected override IReadOnlyList<Func<IFinding, IFinding>> ClaimMutations { get; } =
    [
        finding => ((WriteLossFinding)finding) with { ColumnName = "@other" },
        finding => ((WriteLossFinding)finding) with { SourcePath = "dbo.Other" },
    ];

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(Truncating, Truncated, "EXEC dbo.P;"),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("value-fits-the-variable", Fitting, "EXEC dbo.P;"), Truncating, OwnSetup: true),
    ];
}
