using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.ForcedParameterizationDmlOutputListLiteralRuleId)]
public sealed class ForcedParameterizationDmlOutputListLiteralPairedOracleTests : ForcedParameterizationPairedOracle
{
    private const string Kept = "output-list-literal";

    protected override string AbsentLiteral => "99999";

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(Kept, Setup, "UPDATE dbo.T SET Val = Val OUTPUT 7 AS K WHERE Id = -1;"),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("assigned-literal", Setup, "UPDATE dbo.T SET Val = 7 WHERE Id = -1;"), Kept),
    ];
}
