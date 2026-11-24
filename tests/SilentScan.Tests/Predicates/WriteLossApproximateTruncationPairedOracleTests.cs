using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.WriteLossApproximateTruncationRuleId)]
public sealed class WriteLossApproximateTruncationPairedOracleTests : RuleOracle
{
    private const string Lossy = "CREATE PROCEDURE dbo.P AS BEGIN DECLARE @src FLOAT = 3.7; DECLARE @v INT; SET @v = @src; SELECT @v AS V; END\nGO\n";

    private const string Lossless = "CREATE PROCEDURE dbo.P AS BEGIN DECLARE @src INT = 3; DECLARE @v INT; SET @v = @src; SELECT @v AS V; END\nGO\n";

    private const string Narrowed = "value-assigned-to-a-narrower-variable";

    private const string ReturnLossy = "CREATE FUNCTION dbo.F() RETURNS INT AS BEGIN DECLARE @src FLOAT = 3.7; RETURN @src; END\nGO\n";

    private const string ReturnLossless = "CREATE FUNCTION dbo.F() RETURNS INT AS BEGIN DECLARE @src INT = 3; RETURN @src; END\nGO\n";

    private const string DefaultLossy = "CREATE PROCEDURE dbo.P @v INT = 3.7E0 AS BEGIN SELECT @v AS V; END\nGO\n";

    private const string DefaultLossless = "CREATE PROCEDURE dbo.P @v INT = 3 AS BEGIN SELECT @v AS V; END\nGO\n";

    private const string ReturnNarrowed = "value-returned-through-a-narrower-return-type";

    private const string DefaultNarrowed = "default-value-finer-than-the-parameter";

    protected override IReadOnlyList<Func<IFinding, IFinding>> ClaimMutations { get; } =
    [
        finding => ((WriteLossFinding)finding) with { ColumnName = "@other" },
        finding => ((WriteLossFinding)finding) with { SourcePath = "dbo.Other" },
    ];

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(Narrowed, Lossy, "EXEC dbo.P;"),
        new(ReturnNarrowed, ReturnLossy, "SELECT dbo.F() AS V;"),
        new(DefaultNarrowed, DefaultLossy, "EXEC dbo.P;"),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("value-fits-the-variable", Lossless, "EXEC dbo.P;"), Narrowed, OwnSetup: true),
        new(new("value-fits-the-return-type", ReturnLossless, "SELECT dbo.F() AS V;"), ReturnNarrowed, OwnSetup: true),
        new(new("default-value-fits-the-parameter", DefaultLossless, "EXEC dbo.P;"), DefaultNarrowed, OwnSetup: true),
    ];
}
