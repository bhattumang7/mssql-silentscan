using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.CartesianJoinExplicitCrossJoinRuleId)]
public sealed class CartesianJoinExplicitCrossJoinPairedOracleTests : RuleOracle
{
    private const string Setup = """
        CREATE TABLE dbo.A (Id INT NULL);
        CREATE TABLE dbo.B (Id INT NULL);
        GO
        INSERT INTO dbo.A (Id) VALUES (1), (2), (3);
        INSERT INTO dbo.B (Id) VALUES (1), (2);
        GO
        """;

    private const string Product = "tables-joined-without-a-predicate";

    protected override IReadOnlyList<Func<IFinding, IFinding>> ClaimMutations { get; } =
    [
        finding => ((CartesianJoinFinding)finding) with { SecondTableQualifiedName = "dbo.Other" },
    ];

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(Product, Setup, "SELECT a.Id AS AId, b.Id AS BId FROM [dbo].[A] a CROSS JOIN [dbo].[B] b;"),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("tables-joined-on-a-key", Setup, "SELECT a.Id AS AId, b.Id AS BId FROM [dbo].[A] a CROSS JOIN [dbo].[B] b WHERE a.Id = b.Id;"), Product),
    ];
}
