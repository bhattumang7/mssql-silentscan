using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.CompositeIndexLeadingColumnRuleId)]
public sealed class CompositeIndexLeadingColumnPairedOracleTests : RuleOracle
{
    private const string Setup = """
        CREATE TABLE dbo.T (Id INT NOT NULL PRIMARY KEY, A INT NOT NULL, B INT NOT NULL, Pad CHAR(200) NOT NULL DEFAULT 'x');
        CREATE INDEX IX_T_AB ON dbo.T (A, B);
        GO
        INSERT INTO dbo.T (Id, A, B)
        SELECT TOP (5000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)), ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) % 50, ROW_NUMBER() OVER (ORDER BY (SELECT NULL))
        FROM sys.all_objects a CROSS JOIN sys.all_objects b;
        GO
        """;

    private const string NonLeadingOnly = "non-leading-column-only";

    protected override IReadOnlyList<Func<IFinding, IFinding>> ClaimMutations { get; } =
    [
        finding => ((CompositeIndexLeadingColumnFinding)finding) with { TableQualifiedName = "dbo.Other" },
    ];

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(NonLeadingOnly, Setup, "SELECT Id FROM dbo.T WHERE B = 5;"),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("leading-column-bound", Setup, "SELECT Id FROM dbo.T WHERE A = 3 AND B > 0;"), NonLeadingOnly),
    ];
}
