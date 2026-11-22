using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.IndexCoverageKeyLookupProneIndexRuleId)]
public sealed class KeyLookupPronePairedOracleTests : RuleOracle
{
    private const string Setup = """
        CREATE TABLE dbo.T (Id INT NOT NULL PRIMARY KEY, A INT NOT NULL, Pad CHAR(200) NOT NULL DEFAULT 'x');
        CREATE INDEX IX_T_A ON dbo.T (A);
        GO
        INSERT INTO dbo.T (Id, A)
        SELECT TOP (5000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)), ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) % 500
        FROM sys.all_objects a CROSS JOIN sys.all_objects b;
        GO
        """;

    private const string Uncovered = "seek-with-uncovered-column";

    protected override IReadOnlyList<Func<IFinding, IFinding>> ClaimMutations { get; } =
    [
        finding => ((IndexCoverageFinding)finding) with { IndexName = "IX_Missing" },
        finding => ((IndexCoverageFinding)finding) with { UncoveredColumns = [] },
    ];

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(Uncovered, Setup, "SELECT Pad FROM dbo.T WHERE A = 3;"),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("covered-by-key", Setup, "SELECT A FROM dbo.T WHERE A = 3;"), Uncovered),
    ];
}
