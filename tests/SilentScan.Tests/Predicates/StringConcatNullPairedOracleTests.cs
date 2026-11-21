using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.StringConcatNullRuleId)]
public sealed class StringConcatNullPairedOracleTests : RuleOracle
{
    private const string Setup = """
        CREATE TABLE dbo.T (Id INT NOT NULL, A VARCHAR(10) NULL, B VARCHAR(10) NOT NULL, C VARCHAR(10) NOT NULL);
        GO
        INSERT INTO dbo.T (Id, A, B, C) VALUES (1, 'x', 'y', 'z'), (2, NULL, 'p', 'q');
        GO
        """;

    private const string TwoOperands = "select-concat-nullable-column";

    private const string ThreeOperands = "three-operand-chain";

    private static IFinding OverNotNullColumn(IFinding finding) =>
        ((StringConcatNullFinding)finding) with { ColumnName = "C" };

    protected override IReadOnlyList<Func<IFinding, IFinding>> ClaimMutations { get; } =
    [
        OverNotNullColumn,
        finding => ((StringConcatNullFinding)finding) with { TableQualifiedName = "dbo.Other" },
    ];

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(TwoOperands, Setup, "SELECT B + A AS Joined FROM dbo.T;"),
        new(ThreeOperands, Setup, "SELECT B + ' ' + A AS Joined FROM dbo.T;"),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("column-not-null", Setup, "SELECT B + C AS Joined FROM dbo.T;"), TwoOperands, OverNotNullColumn),
        new(new("isnull-guard", Setup, "SELECT B + ISNULL(A, '') AS Joined FROM dbo.T;"), TwoOperands),
        new(new("coalesce-guard", Setup, "SELECT B + COALESCE(A, '') AS Joined FROM dbo.T;"), TwoOperands),
        new(new("is-not-null-filter", Setup, "SELECT B + A AS Joined FROM dbo.T WHERE A IS NOT NULL;"), TwoOperands),
        new(new("null-rejecting-comparison", Setup, "SELECT B + A AS Joined FROM dbo.T WHERE A <> 'zzz';"), TwoOperands),
        new(new("null-rejecting-like", Setup, "SELECT B + A AS Joined FROM dbo.T WHERE A LIKE '%';"), TwoOperands),
        new(new("null-rejecting-in", Setup, "SELECT B + A AS Joined FROM dbo.T WHERE A IN ('', 'x');"), TwoOperands),
    ];
}
