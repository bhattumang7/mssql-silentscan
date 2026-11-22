using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.DeprecatedSyntaxEqualsNullComparisonRuleId)]
public sealed class EqualsNullComparisonPairedOracleTests : RuleOracle
{
    private const string Setup = """
        CREATE TABLE dbo.T (Id INT NOT NULL, A INT NULL);
        GO
        INSERT INTO dbo.T (Id, A) VALUES (1, 5), (2, NULL), (3, 7);
        GO
        """;

    private const string Comparison = "null-comparison";

    protected override IReadOnlyList<string> ScannerOnlyForms { get; } = [StatementForms.Procedure, StatementForms.View];

    protected override IReadOnlyList<Func<IFinding, IFinding>> ClaimMutations { get; } =
    [
        finding => ((DeprecatedSyntaxFinding)finding) with { DetailText = "\"<> NULL\"/\"!= NULL\" never matches any row." },
    ];

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(Comparison, Setup, "SELECT Id FROM dbo.T WHERE A = NULL;"),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("is-null-form", Setup, "SELECT Id FROM dbo.T WHERE A IS NULL;"), Comparison),
        new(
            new(
                "module-created-with-ansi-nulls-off",
                Setup + "\nSET ANSI_NULLS OFF;\nGO\nCREATE PROCEDURE dbo.Legacy AS SELECT Id FROM dbo.T WHERE A = NULL;\nGO\nSET ANSI_NULLS ON;\nGO\n",
                "EXEC dbo.Legacy;"),
            Comparison,
            OwnSetup: true),
    ];
}
