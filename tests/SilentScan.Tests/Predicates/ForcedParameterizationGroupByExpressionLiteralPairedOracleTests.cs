using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.ForcedParameterizationGroupByExpressionLiteralRuleId)]
public sealed class ForcedParameterizationGroupByExpressionLiteralPairedOracleTests : ForcedParameterizationPairedOracle
{
    private const string Kept = "group-by-expression-value";

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(Kept, Setup, Splicing("N'SELECT COUNT(*) FROM dbo.T GROUP BY CHARINDEX(''' + CAST(@v AS NVARCHAR(10)) + N''', Name)'")),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("comparison-value", Setup, Splicing("N'SELECT COUNT(*) FROM dbo.T WHERE Name = ''' + CAST(@v AS NVARCHAR(10)) + N''' GROUP BY Val'")), Kept),
    ];
}
