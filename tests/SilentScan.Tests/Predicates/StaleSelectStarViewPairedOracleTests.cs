using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.StaleSelectStarViewRuleId)]
public sealed class StaleSelectStarViewPairedOracleTests : RuleOracle
{
    private const string Base = """
        CREATE TABLE dbo.T (Id INT NOT NULL, A INT NOT NULL);
        GO
        INSERT INTO dbo.T (Id, A) VALUES (1, 10), (2, 20);
        GO
        """;

    private const string StaleView = Base + "\nCREATE VIEW dbo.V AS SELECT * FROM dbo.T;\nGO\nALTER TABLE dbo.T ADD B INT NOT NULL DEFAULT 7;\nGO\n";

    private const string FreshView = Base + "\nALTER TABLE dbo.T ADD B INT NOT NULL DEFAULT 7;\nGO\nCREATE VIEW dbo.V AS SELECT * FROM dbo.T;\nGO\n";

    private const string Stale = "view-compiled-before-the-column-was-added";

    private const string Statement = "SELECT * FROM dbo.V;";

    protected override IReadOnlyList<string> ScannerOnlyForms { get; } = [StatementForms.View];

    protected override IReadOnlyList<Func<IFinding, IFinding>> ClaimMutations { get; } =
    [
        finding => ((StaleSelectStarViewFinding)finding) with { ViewQualifiedName = "dbo.Other" },
    ];

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(Stale, StaleView, Statement),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("view-compiled-after-the-column-was-added", FreshView, Statement), Stale, OwnSetup: true),
    ];
}
