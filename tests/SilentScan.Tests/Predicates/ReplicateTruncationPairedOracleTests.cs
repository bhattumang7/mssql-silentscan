using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.BoundedStringBuiltinTruncationReplicateResultTruncatedRuleId)]
public sealed class ReplicateTruncationPairedOracleTests : RuleOracle
{
    private const string NonUnicode = "literal-repeated-past-cap";

    private const string Unicode = "national-literal-repeated-past-cap";

    private static readonly BoundedStringBuiltinTruncationFinding Claim =
        new(BoundedStringBuiltinTruncationFindingKind.ReplicateResultTruncated, "REPLICATE", 10000, 8000, "claimed.sql", 1, 1);

    protected override IReadOnlyList<string> ExcludedForms { get; } = ["procedure", "view"];

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(NonUnicode, string.Empty, "SELECT REPLICATE('ab', 5000) AS V;"),
        new(Unicode, string.Empty, "SELECT REPLICATE(N'ab', 3000) AS V;"),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("repeated-within-cap", string.Empty, "SELECT REPLICATE('ab', 100) AS V;"), Claim, NonUnicode),
        new(new("national-repeated-within-cap", string.Empty, "SELECT REPLICATE(N'ab', 100) AS V;", "repeated-within-cap"), Claim, Unicode),
        new(new("max-typed-source", string.Empty, "SELECT REPLICATE(CAST('ab' AS VARCHAR(MAX)), 5000) AS V;"), Claim, NonUnicode),
    ];
}
