using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.DeprecatedSyntaxEqualsNullComparisonRuleId)]
public sealed class EqualsNullPairedOracleTests : RuleOracle
{
    private const string Setup = """
        CREATE TABLE dbo.T (Id INT NOT NULL, X INT NULL);
        GO
        INSERT INTO dbo.T (Id, X) VALUES (1, 5), (2, NULL);
        GO
        CREATE VIEW dbo.Anchor AS SELECT 1 AS One;
        GO
        """;

    private const string InSelect = "select-where-equals-null";

    private const string InUpdate = "update-where-equals-null";

    private static readonly DeprecatedSyntaxFinding Claim =
        new(DeprecatedSyntaxFindingKind.EqualsNullComparison, "dbo.Anchor", "claimed.sql", 1, 1, "claimed");

    protected override IReadOnlyList<string> ExcludedForms { get; } = ["procedure", "view"];

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(InSelect, Setup, "SELECT Id FROM dbo.T WHERE X = NULL;"),
        new(InUpdate, Setup, "UPDATE dbo.T SET Id = Id WHERE X = NULL;"),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("is-null", Setup, "SELECT Id FROM dbo.T WHERE X IS NULL;"), Claim, InSelect),
        new(new("update-is-null", Setup, "UPDATE dbo.T SET Id = Id WHERE X IS NULL;", "is-null"), Claim, InUpdate),
    ];
}
