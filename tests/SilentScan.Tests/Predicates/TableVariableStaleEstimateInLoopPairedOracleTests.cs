using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.QueryAntiPatternTableVariableStaleEstimateInLoopRuleId)]
public sealed class TableVariableStaleEstimateInLoopPairedOracleTests : RuleOracle
{
    private const string Stale = "CREATE PROCEDURE dbo.P AS BEGIN DECLARE @t TABLE (Id INT); DECLARE @i INT = 0; DECLARE @c INT; WHILE @i < 300 BEGIN INSERT INTO @t VALUES (@i); SELECT @c = COUNT(Id) FROM @t; SET @i = @i + 1; END; END";

    private const string Refreshed = "CREATE PROCEDURE dbo.P AS BEGIN DECLARE @t TABLE (Id INT); DECLARE @i INT = 0; DECLARE @c INT; WHILE @i < 300 BEGIN INSERT INTO @t VALUES (@i); SELECT @c = COUNT(Id) FROM @t OPTION (RECOMPILE); SET @i = @i + 1; END; END";

    private const string InLoop = "table-variable-read-in-a-growing-loop";

    private const string Statement = "EXEC dbo.P;";

    protected override int? MinimumCompatibilityLevel => 150;

    protected override IReadOnlyList<Func<IFinding, IFinding>> ClaimMutations { get; } =
    [
        finding => ((QueryAntiPatternFinding)finding) with { SourcePath = "dbo.Other" },
        finding => ((QueryAntiPatternFinding)finding) with { DetailText = "@missing" },
    ];

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(InLoop, Stale, Statement),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("statement-with-option-recompile", Refreshed, Statement), InLoop, OwnSetup: true),
    ];
}
