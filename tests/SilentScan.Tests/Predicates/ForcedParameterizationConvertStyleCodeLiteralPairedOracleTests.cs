using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.ForcedParameterizationConvertStyleCodeLiteralRuleId)]
public sealed class ForcedParameterizationConvertStyleCodeLiteralPairedOracleTests : ForcedParameterizationPairedOracle
{
    private const string Kept = "convert-style-value";

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(Kept, Setup, Splicing("N'SELECT CONVERT(VARCHAR(30), D, ' + CAST(@v AS NVARCHAR(10)) + N') FROM dbo.T'")),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("comparison-value", Setup, Splicing("N'SELECT Id FROM dbo.T WHERE Val = ' + CAST(@v AS NVARCHAR(10))")), Kept),
    ];
}
