using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.CheckConstraintNullNotHandledRuleId)]
public sealed class CheckConstraintNullNotHandledPairedOracleTests : RuleOracle
{
    private const string Unguarded = "CREATE TABLE dbo.T (Qty INT NULL CONSTRAINT CK_T_Qty CHECK (Qty > 0));\nGO\n";

    private const string Guarded = "CREATE TABLE dbo.T (Qty INT NULL CONSTRAINT CK_T_Qty CHECK (Qty IS NOT NULL AND Qty > 0));\nGO\n";

    private const string Passes = "check-constraint-without-a-null-test";

    private const string Statement = "INSERT INTO dbo.T (Qty) VALUES (NULL);";

    protected override IReadOnlyList<Func<IFinding, IFinding>> ClaimMutations { get; } =
    [
        finding => ((CheckConstraintFinding)finding) with { TableQualifiedName = "dbo.Other" },
        finding => ((CheckConstraintFinding)finding) with { ColumnName = "Other" },
    ];

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(Passes, Unguarded, Statement),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("check-constraint-with-a-null-test", Guarded, Statement), Passes, OwnSetup: true),
    ];
}
