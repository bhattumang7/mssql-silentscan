using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.ForcedParameterizationHavingLiteralRuleId)]
public sealed class ForcedParameterizationHavingLiteralPairedOracleTests : ForcedParameterizationPairedOracle
{
    private const string Kept = "having-value";

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(Kept, Setup, Splicing("N'SELECT Val FROM dbo.T GROUP BY Val HAVING MAX(Name) > ''' + CAST(@v AS NVARCHAR(10)) + N''''")),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("where-value", Setup, Splicing("N'SELECT Val FROM dbo.T WHERE Name > ''' + CAST(@v AS NVARCHAR(10)) + N''' GROUP BY Val'")), Kept),
    ];
}
