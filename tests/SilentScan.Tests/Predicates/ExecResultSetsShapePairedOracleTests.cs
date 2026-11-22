using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.ExecResultSetsShapeColumnTypeMismatchRuleId)]
public sealed class ExecResultSetsShapePairedOracleTests : RuleOracle
{
    private const string Callee = "CREATE PROCEDURE dbo.Callee AS SELECT CAST(NCHAR(913) AS NVARCHAR(100)) AS V;\nGO\n";

    private const string Narrow = Callee + "CREATE PROCEDURE dbo.Caller AS BEGIN EXEC dbo.Callee WITH RESULT SETS ((V VARCHAR(50))); END\nGO\n";

    private const string Matching = Callee + "CREATE PROCEDURE dbo.Caller AS BEGIN EXEC dbo.Callee WITH RESULT SETS ((V NVARCHAR(100))); END\nGO\n";

    private const string Mismatch = "result-sets-column-non-unicode-for-a-unicode-described-column";

    private const string Statement = "EXEC dbo.Caller;";

    protected override IReadOnlyList<Func<IFinding, IFinding>> ClaimMutations { get; } =
    [
        finding => ((ExecResultSetsShapeFinding)finding) with { CallerScopeQualifiedName = "dbo.Other" },
        finding => ((ExecResultSetsShapeFinding)finding) with { ColumnName = "Other" },
    ];

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(Mismatch, Narrow, Statement),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("result-sets-column-unicode-like-the-described-column", Matching, Statement), Mismatch, OwnSetup: true),
    ];
}
