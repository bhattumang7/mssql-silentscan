using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.BoundedStringBuiltinTruncationReplaceResultTruncatedRuleId)]
public sealed class ReplaceTruncationPairedOracleTests : RuleOracle
{
    private const string Growing = "growing-replacement-past-cap";

    private static readonly string Input = new('a', 4000);

    private static readonly BoundedStringBuiltinTruncationFinding Claim =
        new(BoundedStringBuiltinTruncationFindingKind.ReplaceResultTruncated, "REPLACE", 12000, 8000, "claimed.sql", 1, 1);

    protected override IReadOnlyList<string> ExcludedForms { get; } = ["procedure", "view"];

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(Growing, string.Empty, $"SELECT REPLACE('{Input}', 'a', 'bbb') AS V;"),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("replacement-not-longer", string.Empty, $"SELECT REPLACE('{Input}', 'a', 'b') AS V;"), Claim, Growing),
        new(new("result-within-cap", string.Empty, "SELECT REPLACE('aaaa', 'a', 'bbb') AS V;"), Claim, Growing),
        new(new("max-typed-input", string.Empty, $"SELECT REPLACE(CAST('{Input}' AS VARCHAR(MAX)), 'a', 'bbb') AS V;"), Claim, Growing),
    ];
}
