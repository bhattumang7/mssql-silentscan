using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.ForcedParameterizationSelectListLiteralRuleId)]
public sealed class ForcedParameterizationSelectListLiteralPairedOracleTests : ForcedParameterizationPairedOracle
{
    private const string Kept = "select-list-literal";

    protected override string AbsentLiteral => "99999";

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(Kept, Setup, "SELECT Id, 7 AS K FROM dbo.T;"),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("comparison-literal", Setup, "SELECT Id FROM dbo.T WHERE Val = 7;"), Kept),
    ];
}
