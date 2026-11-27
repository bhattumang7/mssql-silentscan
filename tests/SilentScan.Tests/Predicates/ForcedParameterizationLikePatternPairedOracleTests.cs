using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.ForcedParameterizationLikePatternLiteralRuleId)]
public sealed class ForcedParameterizationLikePatternPairedOracleTests : ForcedParameterizationPairedOracle
{
    private const string Kept = "like-pattern-value";

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(Kept, Setup, Splicing("N'SELECT Id FROM dbo.T WHERE Name LIKE ''' + CAST(@v AS NVARCHAR(10)) + N'%'''")),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("comparison-value", Setup, Splicing("N'SELECT Id FROM dbo.T WHERE Name = ''' + CAST(@v AS NVARCHAR(10)) + N'%'''")), Kept),
    ];
}
