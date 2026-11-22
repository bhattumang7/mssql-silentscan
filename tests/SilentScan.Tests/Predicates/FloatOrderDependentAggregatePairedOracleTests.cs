using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.FloatOrderDependentAggregateRuleId)]
public sealed class FloatOrderDependentAggregatePairedOracleTests : RuleOracle
{
    private const string Setup = """
        CREATE TABLE dbo.T (Id INT NOT NULL, F FLOAT NOT NULL, I INT NOT NULL);
        GO
        INSERT INTO dbo.T (Id, F, I) VALUES (1, 1e16, 1), (2, 1, 2), (3, -1e16, 3), (4, 1, 4);
        GO
        """;

    private const string FloatSum = "sum-over-float-column";

    protected override IReadOnlyList<Func<IFinding, IFinding>> ClaimMutations { get; } =
    [
        finding => ((FloatOrderDependentAggregateFinding)finding) with { TableQualifiedName = "dbo.Other" },
        finding => ((FloatOrderDependentAggregateFinding)finding) with { ColumnName = "I" },
    ];

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(FloatSum, Setup, "SELECT SUM(F) AS Total FROM dbo.T;"),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("sum-over-int-column", Setup, "SELECT SUM(I) AS Total FROM dbo.T;"), FloatSum),
    ];
}
