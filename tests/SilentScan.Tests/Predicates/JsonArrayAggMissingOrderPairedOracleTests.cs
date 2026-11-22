using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.JsonArrayAggMissingOrderRuleId)]
public sealed class JsonArrayAggMissingOrderPairedOracleTests : RuleOracle
{
    private const string Setup = """
        CREATE TABLE dbo.T (Id INT NOT NULL, Name VARCHAR(20) NOT NULL);
        GO
        INSERT INTO dbo.T (Id, Name) VALUES (1, 'delta'), (2, 'bravo'), (3, 'echo'), (4, 'alpha'), (5, 'charlie');
        GO
        """;

    private const string Unordered = "json-arrayagg-without-order";

    protected override int? MinimumCompatibilityLevel => 170;

    protected override IReadOnlyList<Func<IFinding, IFinding>> ClaimMutations { get; } =
    [
        finding => ((JsonArrayAggMissingOrderFinding)finding) with { TableQualifiedName = "dbo.Other" },
        finding => ((JsonArrayAggMissingOrderFinding)finding) with { ColumnName = null },
    ];

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(Unordered, Setup, "SELECT JSON_ARRAYAGG(Name) AS Joined FROM dbo.T;"),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("json-arrayagg-with-order", Setup, "SELECT JSON_ARRAYAGG(Name ORDER BY Name) AS Joined FROM dbo.T;"), Unordered),
    ];
}
