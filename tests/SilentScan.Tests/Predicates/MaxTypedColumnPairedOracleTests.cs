using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.MaxTypedColumnMaxLengthRuleId)]
public sealed class MaxTypedColumnPairedOracleTests : RuleOracle
{
    private const string Fill = """
        INSERT INTO dbo.T (Id, Code)
        SELECT TOP (30000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)), CAST(ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS VARCHAR(20))
        FROM sys.all_objects a CROSS JOIN sys.all_objects b;
        GO
        """;

    private const string MaxTyped = "CREATE TABLE dbo.T (Id INT NOT NULL, Code VARCHAR(MAX) NULL, Pad CHAR(100) NOT NULL DEFAULT 'x');\nGO\n" + Fill;

    private const string Bounded = "CREATE TABLE dbo.T (Id INT NOT NULL, Code VARCHAR(100) NULL, Pad CHAR(100) NOT NULL DEFAULT 'x');\nGO\n" + Fill;

    private const string Unseekable = "max-typed-column-in-a-predicate";

    private const string Statement = "SELECT Id FROM dbo.T WHERE Code = '1234';";

    protected override IReadOnlyList<Func<IFinding, IFinding>> ClaimMutations { get; } =
    [
        finding => ((MaxTypedColumnFinding)finding) with { TableQualifiedName = "dbo.Other" },
        finding => ((MaxTypedColumnFinding)finding) with { ColumnName = "Other" },
    ];

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(Unseekable, MaxTyped, Statement),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("bounded-column-in-a-predicate", Bounded, Statement), Unseekable, OwnSetup: true),
    ];
}
