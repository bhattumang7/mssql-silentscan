using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.BoundedStringBuiltinTruncationSpaceResultTruncatedRuleId)]
public sealed class SpaceTruncationPairedOracleTests : RuleOracle
{
    private const string PastCap = "count-past-cap";

    private static readonly BoundedStringBuiltinTruncationFinding Claim =
        new(BoundedStringBuiltinTruncationFindingKind.SpaceResultTruncated, "SPACE", 9000, 8000, "claimed.sql", 1, 1);

    protected override IReadOnlyList<string> ExcludedForms { get; } = ["procedure", "view"];

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(PastCap, string.Empty, "SELECT SPACE(9000) AS V;"),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("count-within-cap", string.Empty, "SELECT SPACE(100) AS V;"), Claim, PastCap),
        new(new("count-at-cap", string.Empty, "SELECT SPACE(8000) AS V;"), Claim, PastCap),
    ];
}
