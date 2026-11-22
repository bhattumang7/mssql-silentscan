using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.ProcCallArgumentMismatchRuleId)]
public sealed class ProcCallArgumentMismatchPairedOracleTests : RuleOracle
{
    private const string Short = "CREATE PROCEDURE dbo.Callee @p VARCHAR(3) AS SELECT @p AS V;\nGO\nCREATE PROCEDURE dbo.Caller AS BEGIN DECLARE @v VARCHAR(10) = 'abcdefghij'; EXEC dbo.Callee @v; END\nGO\n";

    private const string Full = "CREATE PROCEDURE dbo.Callee @p VARCHAR(10) AS SELECT @p AS V;\nGO\nCREATE PROCEDURE dbo.Caller AS BEGIN DECLARE @v VARCHAR(10) = 'abcdefghij'; EXEC dbo.Callee @v; END\nGO\n";

    private const string Truncated = "argument-longer-than-the-callee-parameter";

    private const string Statement = "EXEC dbo.Caller;";

    protected override IReadOnlyList<Func<IFinding, IFinding>> ClaimMutations { get; } =
    [
        finding => ((ProcCallArgumentMismatchFinding)finding) with { CalleeQualifiedName = "dbo.Other" },
        finding => ((ProcCallArgumentMismatchFinding)finding) with { FormalParameterName = "@other" },
    ];

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(Truncated, Short, Statement),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("argument-fits-the-callee-parameter", Full, Statement), Truncated, OwnSetup: true),
    ];
}
