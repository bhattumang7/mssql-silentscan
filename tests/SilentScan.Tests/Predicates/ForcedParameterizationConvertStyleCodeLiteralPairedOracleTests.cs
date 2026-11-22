using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.ForcedParameterizationConvertStyleCodeLiteralRuleId)]
public sealed class ForcedParameterizationConvertStyleCodeLiteralPairedOracleTests : ForcedParameterizationPairedOracle
{
    private const string Kept = "convert-style-literal";

    protected override string AbsentLiteral => "99999";

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(Kept, Setup, "SELECT CONVERT(VARCHAR(30), D, 126) AS C FROM dbo.T;"),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("comparison-literal", Setup, "SELECT Id FROM dbo.T WHERE Val = 126;"), Kept),
    ];
}
