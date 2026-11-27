using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.ForcedParameterizationTableSampleSizeLiteralRuleId)]
public sealed class ForcedParameterizationTableSampleSizeLiteralPairedOracleTests : ForcedParameterizationPairedOracle
{
    private const string Kept = "sample-size-value";

    private const string Rows = Setup + """

        INSERT INTO dbo.T (Id, Name, Val) SELECT TOP (1000) 2 + ROW_NUMBER() OVER (ORDER BY (SELECT NULL)), 'abc', 1 FROM sys.all_columns;
        GO
        """;

    protected override IReadOnlyList<OracleCase> FiringCases { get; } =
    [
        new(Kept, Rows, Splicing("N'SELECT Id FROM dbo.T TABLESAMPLE (' + CAST(@v AS NVARCHAR(10)) + N' ROWS)'")),
    ];

    protected override IReadOnlyList<SilentCase> SilentCases { get; } =
    [
        new(new("comparison-value", Rows, Splicing("N'SELECT Id FROM dbo.T TABLESAMPLE (10 ROWS) WHERE Val = ' + CAST(@v AS NVARCHAR(10))")), Kept),
    ];
}
