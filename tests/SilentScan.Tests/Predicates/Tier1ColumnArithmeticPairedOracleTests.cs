using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.Tier1ColumnArithmeticRuleId)]
public sealed class Tier1ColumnArithmeticPairedOracleTests : RuleOracle
{
    private const string Setup = """
        CREATE TABLE dbo.T (Id INT NOT NULL PRIMARY KEY, X INT NOT NULL, Y INT NOT NULL DEFAULT 0, Pad CHAR(200) NOT NULL DEFAULT 'x');
        GO
        INSERT INTO dbo.T (Id, X, Y)
        SELECT n, CASE WHEN n % 1000 = 0 THEN 7 ELSE n % 300 + 100 END, 1
        FROM (SELECT TOP (60000) CAST(ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS INT) AS n FROM sys.all_objects a CROSS JOIN sys.all_objects b) AS numbers;
        GO
        """;

    private const string Arithmetic = "column-arithmetic-predicate";

    protected override IReadOnlyList<Func<IFinding, IFinding>> ClaimMutations { get; } =
    [
        finding => ((SargabilityFinding)finding) with { TableQualifiedName = "dbo.Other" },
        finding => ((SargabilityFinding)finding) with { PredicateFragmentText = null },
    ];

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(Arithmetic, Setup, "SELECT Id FROM dbo.T WHERE X + Y = 8;"),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("residual-beside-a-key-seek", Setup, "SELECT Id FROM dbo.T WHERE Id = 5 AND X + Y = 8;"), Arithmetic),
    ];
}
