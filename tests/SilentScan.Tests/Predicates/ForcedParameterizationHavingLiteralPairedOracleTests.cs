using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.ForcedParameterizationHavingLiteralRuleId)]
public sealed class ForcedParameterizationHavingLiteralPairedOracleTests : ForcedParameterizationPairedOracle
{
    private const string Kept = "having-literal";

    protected override string AbsentLiteral => "99999";

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(Kept, Setup, "SELECT Val FROM dbo.T GROUP BY Val HAVING COUNT(*) > 7;"),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("where-literal", Setup, "SELECT Val FROM dbo.T WHERE Id > 7 GROUP BY Val;"), Kept),
    ];
}
