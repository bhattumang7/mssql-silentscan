using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.SpExecuteSqlParameterMismatchRuleId)]
public sealed class SpExecuteSqlParameterMismatchPairedOracleTests : RuleOracle
{
    private const string Short = "CREATE PROCEDURE dbo.Caller AS BEGIN DECLARE @v VARCHAR(10) = 'abcdefghij'; EXEC sp_executesql N'SELECT @p AS V', N'@p VARCHAR(3)', @p = @v; END\nGO\n";

    private const string Full = "CREATE PROCEDURE dbo.Caller AS BEGIN DECLARE @v VARCHAR(10) = 'abcdefghij'; EXEC sp_executesql N'SELECT @p AS V', N'@p VARCHAR(10)', @p = @v; END\nGO\n";

    private const string Truncated = "argument-longer-than-the-declared-parameter";

    private const string Statement = "EXEC dbo.Caller;";

    protected override IReadOnlyList<Func<IFinding, IFinding>> ClaimMutations { get; } =
    [
        finding => ((SpExecuteSqlParameterMismatchFinding)finding) with { CallerScopeQualifiedName = "dbo.Other" },
        finding => ((SpExecuteSqlParameterMismatchFinding)finding) with { ParameterName = "@other" },
    ];

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(Truncated, Short, Statement),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("argument-fits-the-declared-parameter", Full, Statement), Truncated, OwnSetup: true),
    ];
}
