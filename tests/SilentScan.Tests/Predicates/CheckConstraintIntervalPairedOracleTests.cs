using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.CheckConstraintPredicateContradictionIntervalRuleId)]
public sealed class CheckConstraintIntervalPairedOracleTests : RuleOracle
{
    private const string Setup = """
        CREATE TABLE dbo.T (Id INT NOT NULL, Qty INT NOT NULL, Loose INT NOT NULL);
        GO
        ALTER TABLE dbo.T ADD CONSTRAINT CK_Qty CHECK (Qty BETWEEN 1 AND 10);
        GO
        ALTER TABLE dbo.T WITH NOCHECK ADD CONSTRAINT CK_Loose CHECK (Loose BETWEEN 1 AND 10);
        GO
        """;

    private const string Disjoint = "predicate-disjoint-from-trusted-check";

    private static IFinding OverUntrustedConstraint(IFinding finding) =>
        ((CheckConstraintPredicateContradictionFinding)finding) with { ColumnName = "Loose", ConstraintName = "CK_Loose" };

    protected override IReadOnlyList<Func<IFinding, IFinding>> ClaimMutations { get; } =
    [
        OverUntrustedConstraint,
        finding => ((CheckConstraintPredicateContradictionFinding)finding) with { TableQualifiedName = "dbo.Other" },
    ];

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(Disjoint, Setup, "SELECT Id FROM dbo.T WHERE Qty > 100;"),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("predicate-inside-the-check-interval", Setup, "SELECT Id FROM dbo.T WHERE Qty > 5;"), Disjoint),
        new(new("check-constraint-not-trusted", Setup, "SELECT Id FROM dbo.T WHERE Loose > 100;"), Disjoint, OverUntrustedConstraint),
    ];
}
