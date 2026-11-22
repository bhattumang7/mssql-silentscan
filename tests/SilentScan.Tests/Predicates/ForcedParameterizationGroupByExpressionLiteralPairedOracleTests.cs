using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.ForcedParameterizationGroupByExpressionLiteralRuleId)]
public sealed class ForcedParameterizationGroupByExpressionLiteralPairedOracleTests : ForcedParameterizationPairedOracle
{
    private const string Kept = "group-by-expression-literal";

    protected override string AbsentLiteral => "99999";

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(Kept, Setup, "SELECT COUNT(*) AS C FROM dbo.T GROUP BY Id + 7;"),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("comparison-literal", Setup, "SELECT COUNT(*) AS C FROM dbo.T WHERE Val = 7 GROUP BY Id;"), Kept),
    ];
}
