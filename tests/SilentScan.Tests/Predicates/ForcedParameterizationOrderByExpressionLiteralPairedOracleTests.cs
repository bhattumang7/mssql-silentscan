using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.ForcedParameterizationOrderByExpressionLiteralRuleId)]
public sealed class ForcedParameterizationOrderByExpressionLiteralPairedOracleTests : ForcedParameterizationPairedOracle
{
    private const string Kept = "order-by-expression-literal";

    protected override string AbsentLiteral => "99999";

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(Kept, Setup, "SELECT TOP (3) Id FROM dbo.T ORDER BY Id + 7;"),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("comparison-literal", Setup, "SELECT TOP (3) Id FROM dbo.T WHERE Val = 7 ORDER BY Id;"), Kept),
    ];
}
