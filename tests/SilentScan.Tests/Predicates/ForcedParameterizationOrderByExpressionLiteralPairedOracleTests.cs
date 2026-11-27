using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.ForcedParameterizationOrderByExpressionLiteralRuleId)]
public sealed class ForcedParameterizationOrderByExpressionLiteralPairedOracleTests : ForcedParameterizationPairedOracle
{
    private const string Kept = "order-by-expression-value";

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(Kept, Setup, Splicing("N'SELECT Id FROM dbo.T ORDER BY Val + CHARINDEX(''' + CAST(@v AS NVARCHAR(10)) + N''', Name)'")),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("comparison-value", Setup, Splicing("N'SELECT Id FROM dbo.T WHERE Name = ''' + CAST(@v AS NVARCHAR(10)) + N''''")), Kept),
    ];
}
