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

    private const string Truncating = "result-past-the-non-max-cap";

    protected override IReadOnlyList<string> ScannerOnlyForms { get; } = [StatementForms.Procedure, StatementForms.View];

    protected override IReadOnlyList<Func<IFinding, IFinding>> ClaimMutations { get; } =
    [
        finding => ((BoundedStringBuiltinTruncationFinding)finding) with { FunctionName = "REPLICATE" },
    ];

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(Truncating, Setup, "SELECT LEN(SPACE(9000) + 'x') AS L;"),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("result-within-the-cap", Setup, "SELECT LEN(SPACE(100) + 'x') AS L;"), Truncating),
    ];
}
