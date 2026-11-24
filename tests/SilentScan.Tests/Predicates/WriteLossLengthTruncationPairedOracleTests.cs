using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.WriteLossLengthTruncationRuleId)]
public sealed class WriteLossLengthTruncationPairedOracleTests : RuleOracle
{
    private const string Truncated = "CREATE PROCEDURE dbo.P AS BEGIN DECLARE @src VARCHAR(20) = 'HelloWorldHello'; DECLARE @v VARCHAR(5); SET @v = @src; SELECT @v AS V; END\nGO\n";

    private const string Fitting = "CREATE PROCEDURE dbo.P AS BEGIN DECLARE @src VARCHAR(5) = 'Hello'; DECLARE @v VARCHAR(5); SET @v = @src; SELECT @v AS V; END\nGO\n";

    private const string ReturnTruncated = "CREATE FUNCTION dbo.F() RETURNS VARCHAR(5) AS BEGIN DECLARE @src VARCHAR(20) = 'HelloWorldHello'; RETURN @src; END\nGO\n";

    private const string ReturnFitting = "CREATE FUNCTION dbo.F() RETURNS VARCHAR(5) AS BEGIN DECLARE @src VARCHAR(5) = 'Hello'; RETURN @src; END\nGO\n";

    private const string Truncating = "value-assigned-to-a-shorter-variable";

    private const string ReturnTruncating = "value-returned-through-a-shorter-return-type";

    private const string SelectTruncated = "CREATE PROCEDURE dbo.P AS BEGIN DECLARE @src VARCHAR(20) = 'HelloWorldHello'; DECLARE @v VARCHAR(5); SELECT @v = @src; SELECT @v AS V; END\nGO\n";

    private const string SelectFitting = "CREATE PROCEDURE dbo.P AS BEGIN DECLARE @src VARCHAR(5) = 'Hello'; DECLARE @v VARCHAR(5); SELECT @v = @src; SELECT @v AS V; END\nGO\n";

    private const string DefaultTruncated = "CREATE PROCEDURE dbo.P @v VARCHAR(5) = 'HelloWorldHello' AS BEGIN SELECT @v AS V; END\nGO\n";

    private const string DefaultFitting = "CREATE PROCEDURE dbo.P @v VARCHAR(5) = 'Hello' AS BEGIN SELECT @v AS V; END\nGO\n";

    private const string SelectTruncating = "value-selected-into-a-shorter-variable";

    private const string DefaultTruncating = "default-value-longer-than-the-parameter";

    protected override IReadOnlyList<Func<IFinding, IFinding>> ClaimMutations { get; } =
    [
        finding => ((WriteLossFinding)finding) with { ColumnName = "@other" },
        finding => ((WriteLossFinding)finding) with { SourcePath = "dbo.Other" },
    ];

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(Truncating, Truncated, "EXEC dbo.P;"),
        new(ReturnTruncating, ReturnTruncated, "SELECT dbo.F() AS V;"),
        new(SelectTruncating, SelectTruncated, "EXEC dbo.P;"),
        new(DefaultTruncating, DefaultTruncated, "EXEC dbo.P;"),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("value-fits-the-variable", Fitting, "EXEC dbo.P;"), Truncating, OwnSetup: true),
        new(new("value-fits-the-return-type", ReturnFitting, "SELECT dbo.F() AS V;"), ReturnTruncating, OwnSetup: true),
        new(new("value-selected-fits-the-variable", SelectFitting, "EXEC dbo.P;"), SelectTruncating, OwnSetup: true),
        new(new("default-value-fits-the-parameter", DefaultFitting, "EXEC dbo.P;"), DefaultTruncating, OwnSetup: true),
    ];
}
