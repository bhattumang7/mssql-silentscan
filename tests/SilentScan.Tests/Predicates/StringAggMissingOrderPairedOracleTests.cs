using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.StringAggMissingOrderRuleId)]
public sealed class StringAggMissingOrderPairedOracleTests : RuleOracle
{
    private const string Setup = """
        CREATE TABLE dbo.T (Id INT NOT NULL, Name VARCHAR(20) NOT NULL);
        GO
        INSERT INTO dbo.T (Id, Name) VALUES (1, 'delta'), (2, 'bravo'), (3, 'echo'), (4, 'alpha'), (5, 'charlie');
        GO
        """;

    private const string Unordered = "string-agg-without-order";

    protected override int? MinimumCompatibilityLevel => 140;

    protected override IReadOnlyList<Func<IFinding, IFinding>> ClaimMutations { get; } =
    [
        finding => ((StringAggMissingOrderFinding)finding) with { TableQualifiedName = "dbo.Other" },
        finding => ((StringAggMissingOrderFinding)finding) with { ColumnName = null },
    ];

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(Unordered, Setup, "SELECT STRING_AGG(Name, ',') AS Joined FROM dbo.T;"),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("string-agg-with-order", Setup, "SELECT STRING_AGG(Name, ',') WITHIN GROUP (ORDER BY Name) AS Joined FROM dbo.T;"), Unordered),
    ];
}
