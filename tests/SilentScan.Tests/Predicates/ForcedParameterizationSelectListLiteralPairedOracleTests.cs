using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.ForcedParameterizationSelectListLiteralRuleId)]
public sealed class ForcedParameterizationSelectListLiteralPairedOracleTests : ForcedParameterizationPairedOracle
{
    private const string Kept = "select-list-value";

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(Kept, Setup, Splicing("N'SELECT ''' + CAST(@v AS NVARCHAR(10)) + N''' AS Tag, Id FROM dbo.T'")),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("comparison-value", Setup, Splicing("N'SELECT Id FROM dbo.T WHERE Name = ''' + CAST(@v AS NVARCHAR(10)) + N''''")), Kept),
    ];
}
