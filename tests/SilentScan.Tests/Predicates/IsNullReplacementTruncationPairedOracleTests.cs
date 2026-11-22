using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.IsNullReplacementValueTruncationRuleId)]
public sealed class IsNullReplacementTruncationPairedOracleTests : RuleOracle
{
    private const string Setup = """
        CREATE TABLE dbo.T (Id INT NOT NULL PRIMARY KEY, ShortCode VARCHAR(5) NULL, LongCode VARCHAR(50) NULL);
        GO
        INSERT INTO dbo.T (Id, ShortCode, LongCode) VALUES (1, NULL, 'A much longer fallback description');
        GO
        """;

    private const string Wide = """
        CREATE TABLE dbo.T (Id INT NOT NULL PRIMARY KEY, ShortCode VARCHAR(50) NULL, LongCode VARCHAR(50) NULL);
        GO
        INSERT INTO dbo.T (Id, ShortCode, LongCode) VALUES (1, NULL, 'A much longer fallback description');
        GO
        """;

    private const string Truncating = "length-truncation";

    protected override IReadOnlyList<string> ScannerOnlyForms { get; } = [StatementForms.Procedure, StatementForms.View];

    protected override IReadOnlyList<Func<IFinding, IFinding>> ClaimMutations { get; } =
    [
        finding => ((IsNullReplacementValueTruncationFinding)finding) with { CheckExpressionDisplay = "Other" },
        finding => ((IsNullReplacementValueTruncationFinding)finding) with { ReplacementValueDisplay = "Other" },
    ];

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(Truncating, Setup, "SELECT ISNULL(ShortCode, LongCode) AS V FROM dbo.T;"),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("replacement-not-wider", Wide, "SELECT ISNULL(ShortCode, LongCode) AS V FROM dbo.T;"), Truncating, OwnSetup: true),
    ];
}
