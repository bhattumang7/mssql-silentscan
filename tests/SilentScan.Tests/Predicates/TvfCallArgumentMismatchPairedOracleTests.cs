using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.TvfCallArgumentMismatchRuleId)]
public sealed class TvfCallArgumentMismatchPairedOracleTests : RuleOracle
{
    private const string Short = "CREATE FUNCTION dbo.Fn (@p VARCHAR(3)) RETURNS TABLE AS RETURN SELECT @p AS V;\nGO\nCREATE PROCEDURE dbo.Caller AS BEGIN DECLARE @v VARCHAR(10) = 'abcdefghij'; SELECT V FROM dbo.Fn(@v); END\nGO\n";

    private const string Full = "CREATE FUNCTION dbo.Fn (@p VARCHAR(10)) RETURNS TABLE AS RETURN SELECT @p AS V;\nGO\nCREATE PROCEDURE dbo.Caller AS BEGIN DECLARE @v VARCHAR(10) = 'abcdefghij'; SELECT V FROM dbo.Fn(@v); END\nGO\n";

    private const string Truncated = "argument-longer-than-the-function-parameter";

    private const string Statement = "EXEC dbo.Caller;";

    protected override IReadOnlyList<Func<IFinding, IFinding>> ClaimMutations { get; } =
    [
        finding => ((TvfCallArgumentMismatchFinding)finding) with { CalleeQualifiedName = "dbo.Other" },
        finding => ((TvfCallArgumentMismatchFinding)finding) with { FormalParameterName = "@other" },
    ];

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(Truncated, Short, Statement),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("argument-fits-the-function-parameter", Full, Statement), Truncated, OwnSetup: true),
    ];
}
