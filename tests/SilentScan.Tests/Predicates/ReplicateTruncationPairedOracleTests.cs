using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.BoundedStringBuiltinTruncationReplicateResultTruncatedRuleId)]
public sealed class ReplicateTruncationPairedOracleTests : RuleOracle
{
    private const string Setup = """
        CREATE TABLE dbo.T (Id INT NOT NULL PRIMARY KEY);
        GO
        """;

    private const string Truncating = "literal-repeated-past-cap";

    protected override IReadOnlyList<string> ScannerOnlyForms { get; } = [StatementForms.Procedure, StatementForms.View];

    protected override IReadOnlyList<Func<IFinding, IFinding>> ClaimMutations { get; } =
    [
        finding => ((BoundedStringBuiltinTruncationFinding)finding) with { FunctionName = "SPACE" },
    ];

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(Truncating, Setup, "SELECT LEN(REPLICATE('abcdefghij', 900)) AS L;"),
        new("fractional-count-past-cap", Setup, "SELECT LEN(REPLICATE('abcdefghij', 900.5)) AS L;"),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("repeated-within-cap", Setup, "SELECT LEN(REPLICATE('abcdefghij', 100)) AS L;"), Truncating),
        new(new("fractional-count-truncating-to-cap", Setup, "SELECT LEN(REPLICATE('abcdefghij', 800.9)) AS L;"), "fractional-count-past-cap"),
    ];
}
