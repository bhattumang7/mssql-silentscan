using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.ForcedParameterizationLikePatternLiteralRuleId)]
public sealed class ForcedParameterizationLikePatternPairedOracleTests : ForcedParameterizationPairedOracle
{
    private const string Kept = "like-pattern-literal";

    protected override string AbsentLiteral => "'zzz'";

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(Kept, Setup, "SELECT Id FROM dbo.T WHERE Name LIKE 'ab%';"),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("comparison-literal", Setup, "SELECT Id FROM dbo.T WHERE Name = 'ab%';"), Kept),
    ];
}
