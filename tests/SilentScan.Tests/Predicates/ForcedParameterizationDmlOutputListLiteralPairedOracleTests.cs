using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.ForcedParameterizationDmlOutputListLiteralRuleId)]
public sealed class ForcedParameterizationDmlOutputListLiteralPairedOracleTests : ForcedParameterizationPairedOracle
{
    private const string Kept = "output-list-value";

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(Kept, Setup, Splicing("N'UPDATE dbo.T SET Val = 1 OUTPUT ''' + CAST(@v AS NVARCHAR(10)) + N''' AS Tag'")),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("assigned-value", Setup, Splicing("N'UPDATE dbo.T SET Name = ''' + CAST(@v AS NVARCHAR(10)) + N''''")), Kept),
    ];
}
