using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.ForcedParameterizationTopOrPagingLiteralRuleId)]
public sealed class ForcedParameterizationTopOrPagingLiteralPairedOracleTests : ForcedParameterizationPairedOracle
{
    private const string Kept = "top-row-count-literal";

    protected override string AbsentLiteral => "99999";

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(Kept, Setup, "SELECT TOP 7 Id FROM dbo.T;"),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("comparison-literal", Setup, "SELECT Id FROM dbo.T WHERE Val = 7;"), Kept),
    ];
}
