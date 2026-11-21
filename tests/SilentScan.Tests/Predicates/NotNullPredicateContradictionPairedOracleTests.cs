using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.NotNullPredicateContradictionRuleId)]
public sealed class NotNullPredicateContradictionPairedOracleTests : RuleOracle
{
    private const string Setup = """
        CREATE TABLE dbo.T (Id INT NOT NULL, Req VARCHAR(10) NOT NULL, Opt VARCHAR(10) NULL);
        GO
        INSERT INTO dbo.T (Id, Req, Opt) VALUES (1, 'x', 'y'), (2, 'p', NULL);
        GO
        """;

    private const string IsNullOnRequired = "is-null-on-not-null-column";

    private static IFinding OverNullableColumn(IFinding finding) =>
        ((CheckConstraintPredicateContradictionFinding)finding) with { ColumnName = "Opt" };

    protected override IReadOnlyList<Func<IFinding, IFinding>> ClaimMutations { get; } =
    [
        OverNullableColumn,
        finding => ((CheckConstraintPredicateContradictionFinding)finding) with { TableQualifiedName = "dbo.Other" },
    ];

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(IsNullOnRequired, Setup, "SELECT Id FROM dbo.T WHERE Req IS NULL;"),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("is-null-on-nullable-column", Setup, "SELECT Id FROM dbo.T WHERE Opt IS NULL;"), IsNullOnRequired, OverNullableColumn),
        new(new("is-not-null-test", Setup, "SELECT Id FROM dbo.T WHERE Req IS NOT NULL;"), IsNullOnRequired),
    ];
}
