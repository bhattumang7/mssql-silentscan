using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.ForcedParameterizationTopOrPagingLiteralRuleId)]
public sealed class ForcedParameterizationTopOrPagingLiteralPairedOracleTests : ForcedParameterizationPairedOracle
{
    private const string Kept = "top-row-count-value";

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(Kept, Setup, Splicing("N'SELECT TOP (' + CAST(@v AS NVARCHAR(10)) + N') Id FROM dbo.T'")),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("comparison-value", Setup, Splicing("N'SELECT Id FROM dbo.T WHERE Val = ' + CAST(@v AS NVARCHAR(10))")), Kept),
    ];
}
