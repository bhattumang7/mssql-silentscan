using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.Tier1DateFunctionOnColumnRuleId)]
public sealed class Tier1DateFunctionPairedOracleTests : RuleOracle
{
    private const string Setup = """
        CREATE TABLE dbo.T (Id INT NOT NULL PRIMARY KEY, X DATETIME2 NOT NULL, Pad CHAR(200) NOT NULL DEFAULT 'x');
        GO
        INSERT INTO dbo.T (Id, X)
        SELECT n, CASE WHEN n % 1000 = 0 THEN CAST('2020-06-15' AS DATETIME2) ELSE DATEADD(DAY, n % 300, CAST('2000-01-01' AS DATETIME2)) END
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
        new(Wrapped, Setup, "SELECT Id FROM dbo.T WHERE YEAR(X) = 2020;"),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("residual-beside-a-key-seek", Setup, "SELECT Id FROM dbo.T WHERE Id = 5 AND YEAR(X) = 2020;"), Wrapped),
    ];
}
