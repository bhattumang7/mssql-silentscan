using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.WriteLossNumericScaleNarrowingRuleId)]
public sealed class WriteLossNumericScaleNarrowingPairedOracleTests : RuleOracle
{
    private const string Lossy = "CREATE PROCEDURE dbo.P AS BEGIN DECLARE @src DECIMAL(10,4) = 123.4567; DECLARE @v DECIMAL(5,2); SET @v = @src; SELECT CAST(@v AS DECIMAL(38, 4)) AS V; END\nGO\n";

    private const string Lossless = "CREATE PROCEDURE dbo.P AS BEGIN DECLARE @src DECIMAL(5,2) = 123.45; DECLARE @v DECIMAL(5,2); SET @v = @src; SELECT CAST(@v AS DECIMAL(38, 4)) AS V; END\nGO\n";

    private const string ReturnLossy = "CREATE FUNCTION dbo.F() RETURNS DECIMAL(5,2) AS BEGIN DECLARE @src DECIMAL(10,4) = 123.4567; RETURN @src; END\nGO\n";

    private const string ReturnLossless = "CREATE FUNCTION dbo.F() RETURNS DECIMAL(5,2) AS BEGIN DECLARE @src DECIMAL(5,2) = 123.45; RETURN @src; END\nGO\n";

    private const string Narrowed = "value-assigned-to-a-narrower-variable";

    private const string ReturnNarrowed = "value-returned-through-a-narrower-return-type";

    private const string DefaultLossy = "CREATE PROCEDURE dbo.P @v DECIMAL(5,2) = 123.4567 AS BEGIN SELECT CAST(@v AS DECIMAL(38, 4)) AS V; END\nGO\n";

    private const string DefaultLossless = "CREATE PROCEDURE dbo.P @v DECIMAL(5,2) = 123.45 AS BEGIN SELECT CAST(@v AS DECIMAL(38, 4)) AS V; END\nGO\n";

    private const string DefaultNarrowed = "default-value-finer-than-the-parameter";

    protected override IReadOnlyList<Func<IFinding, IFinding>> ClaimMutations { get; } =
    [
        finding => ((WriteLossFinding)finding) with { ColumnName = "@other" },
        finding => ((WriteLossFinding)finding) with { SourcePath = "dbo.Other" },
    ];

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(Narrowed, Lossy, "EXEC dbo.P;"),
        new(ReturnNarrowed, ReturnLossy, "SELECT CAST(dbo.F() AS DECIMAL(38, 4)) AS V;"),
        new(DefaultNarrowed, DefaultLossy, "EXEC dbo.P;"),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("value-fits-the-variable", Lossless, "EXEC dbo.P;"), Narrowed, OwnSetup: true),
        new(new("value-fits-the-return-type", ReturnLossless, "SELECT CAST(dbo.F() AS DECIMAL(38, 4)) AS V;"), ReturnNarrowed, OwnSetup: true),
        new(new("default-value-fits-the-parameter", DefaultLossless, "EXEC dbo.P;"), DefaultNarrowed, OwnSetup: true),
    ];
}
