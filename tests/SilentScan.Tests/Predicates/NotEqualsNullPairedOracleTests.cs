using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.DeprecatedSyntaxNotEqualsNullComparisonRuleId)]
public sealed class NotEqualsNullPairedOracleTests : RuleOracle
{
    private const string Setup = """
        CREATE TABLE dbo.T (Id INT NOT NULL, X INT NULL);
        GO
        INSERT INTO dbo.T (Id, X) VALUES (1, 5), (2, NULL);
        GO
        CREATE VIEW dbo.Anchor AS SELECT 1 AS One;
        GO
        """;

    private const string AngleBrackets = "select-where-angle-bracket-null";

    private const string Bang = "select-where-bang-null";

    private static readonly DeprecatedSyntaxFinding Claim =
        new(DeprecatedSyntaxFindingKind.NotEqualsNullComparison, "dbo.Anchor", "claimed.sql", 1, 1, "claimed");

    protected override IReadOnlyList<string> ExcludedForms { get; } = ["procedure", "view"];

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(AngleBrackets, Setup, "SELECT Id FROM dbo.T WHERE X <> NULL;"),
        new(Bang, Setup, "SELECT Id FROM dbo.T WHERE X != NULL;"),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("is-not-null", Setup, "SELECT Id FROM dbo.T WHERE X IS NOT NULL;"), Claim, AngleBrackets),
        new(new("bang-partner-is-not-null", Setup, "SELECT Id FROM dbo.T WHERE X IS NOT NULL;", "is-not-null"), Claim, Bang),
    ];
}
