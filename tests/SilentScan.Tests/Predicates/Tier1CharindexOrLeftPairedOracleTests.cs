using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.Tier1CharindexOrLeftOnColumnRuleId)]
public sealed class Tier1CharindexOrLeftPairedOracleTests : RuleOracle
{
    private const string Setup = """
        CREATE TABLE dbo.T (Id INT NOT NULL PRIMARY KEY, X VARCHAR(20) NOT NULL, Pad CHAR(200) NOT NULL DEFAULT 'x');
        GO
        INSERT INTO dbo.T (Id, X)
        SELECT n, 'k' + RIGHT('00000000' + CAST(n AS VARCHAR(10)), 8)
        FROM (SELECT TOP (60000) CAST(ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS INT) AS n FROM sys.all_objects a CROSS JOIN sys.all_objects b) AS numbers;
        GO
        """;

    private const string Wrapped = "wrapped-column-predicate";

    protected override IReadOnlyList<Func<IFinding, IFinding>> ClaimMutations { get; } =
    [
        finding => ((SargabilityFinding)finding) with { TableQualifiedName = "dbo.Other" },
        finding => ((SargabilityFinding)finding) with { PredicateFragmentText = null },
    ];

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(Wrapped, Setup, "SELECT Id FROM dbo.T WHERE LEFT(X, 9) = 'k00000005';"),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("residual-beside-a-key-seek", Setup, "SELECT Id FROM dbo.T WHERE Id = 5 AND LEFT(X, 9) = 'k00000005';"), Wrapped),
    ];
}
