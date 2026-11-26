using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.BoundedStringBuiltinTruncationSpaceResultTruncatedRuleId)]
public sealed class SpaceTruncationPairedOracleTests : RuleOracle
{
    private const string Setup = """
        CREATE TABLE dbo.T (Id INT NOT NULL PRIMARY KEY);
        GO
        """;

    private const string Truncating = "count-past-cap";

    protected override IReadOnlyList<string> ScannerOnlyForms { get; } = [StatementForms.Procedure, StatementForms.View];

    protected override IReadOnlyList<Func<IFinding, IFinding>> ClaimMutations { get; } =
    [
        finding => ((BoundedStringBuiltinTruncationFinding)finding) with { FunctionName = "REPLICATE" },
    ];

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(Truncating, Setup, "SELECT LEN(SPACE(9000) + 'x') AS L;"),
        new("fractional-count-past-cap", Setup, "SELECT LEN(SPACE(9000.5) + 'x') AS L;", "count-past-cap"),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("count-within-cap", Setup, "SELECT LEN(SPACE(100) + 'x') AS L;"), Truncating),
        new(new("fractional-count-within-cap", Setup, "SELECT LEN(SPACE(7000.9) + 'x') AS L;", "count-within-cap"), "fractional-count-past-cap"),
    ];
}
