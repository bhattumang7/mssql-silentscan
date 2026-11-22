using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.IndexHintHintedIndexNotSeekableRuleId)]
public sealed class HintedIndexNotSeekablePairedOracleTests : RuleOracle
{
    private const string Base = """
        CREATE TABLE dbo.T (Id INT NOT NULL PRIMARY KEY, A INT NOT NULL, B INT NOT NULL, Pad CHAR(100) NOT NULL DEFAULT 'x');
        GO
        INSERT INTO dbo.T (Id, A, B)
        SELECT TOP (60000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)), ROW_NUMBER() OVER (ORDER BY (SELECT NULL)), ROW_NUMBER() OVER (ORDER BY (SELECT NULL))
        FROM sys.all_objects a CROSS JOIN sys.all_objects b;
        CREATE INDEX IX_T_A ON dbo.T (A);
        CREATE INDEX IX_T_B ON dbo.T (B);
        GO
        """;

    private const string Unbound = Base + "\nCREATE PROCEDURE dbo.P AS SELECT Id FROM dbo.T WITH (INDEX(IX_T_B)) WHERE A = 5;\nGO\n";

    private const string Bound = Base + "\nCREATE PROCEDURE dbo.P AS SELECT Id FROM dbo.T WITH (INDEX(IX_T_B)) WHERE B = 5;\nGO\n";

    private const string Hinted = "hint-without-the-leading-column-bound";

    private const string Statement = "EXEC dbo.P;";

    protected override IReadOnlyList<Func<IFinding, IFinding>> ClaimMutations { get; } =
    [
        finding => ((IndexHintFinding)finding) with { SourcePath = "dbo.Other" },
        finding => ((IndexHintFinding)finding) with { HintedIndexName = "IX_Missing" },
    ];

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(Hinted, Unbound, Statement),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("hint-with-the-leading-column-bound", Bound, Statement), Hinted, OwnSetup: true),
    ];
}
