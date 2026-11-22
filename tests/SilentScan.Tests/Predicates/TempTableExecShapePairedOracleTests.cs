using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.TempTableExecShapeColumnTypeMismatchRuleId)]
public sealed class TempTableExecShapePairedOracleTests : RuleOracle
{
    private const string Callee = "CREATE PROCEDURE dbo.Callee AS SELECT CAST(NCHAR(913) AS NVARCHAR(100)) AS V;\nGO\n";

    private const string Narrow = Callee + "CREATE PROCEDURE dbo.Caller AS BEGIN CREATE TABLE #t (V VARCHAR(50)); INSERT INTO #t EXEC dbo.Callee; SELECT V FROM #t; END\nGO\n";

    private const string Matching = Callee + "CREATE PROCEDURE dbo.Caller AS BEGIN CREATE TABLE #t (V NVARCHAR(100)); INSERT INTO #t EXEC dbo.Callee; SELECT V FROM #t; END\nGO\n";

    private const string Mismatch = "temp-table-column-non-unicode-for-a-unicode-described-column";

    private const string Statement = "EXEC dbo.Caller;";

    protected override IReadOnlyList<Func<IFinding, IFinding>> ClaimMutations { get; } =
    [
        finding => ((TempTableExecShapeFinding)finding) with { CallerScopeQualifiedName = "dbo.Other" },
        finding => ((TempTableExecShapeFinding)finding) with { ColumnName = "Other" },
    ];

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(Mismatch, Narrow, Statement),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("temp-table-column-unicode-like-the-described-column", Matching, Statement), Mismatch, OwnSetup: true),
    ];
}
