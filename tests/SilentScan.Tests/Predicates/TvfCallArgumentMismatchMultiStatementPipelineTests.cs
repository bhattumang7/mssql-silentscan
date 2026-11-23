using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.TvfCallArgumentMismatchRuleId)]
public sealed class TvfCallArgumentMismatchMultiStatementPipelineTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(TvfCallArgumentMismatchMultiStatementPipelineTests);

    private const string Sql = """
        CREATE FUNCTION dbo.fn_Multi (@Code VARCHAR(3))
        RETURNS @t TABLE (Code VARCHAR(10))
        AS
        BEGIN
            INSERT @t VALUES (@Code);
            RETURN;
        END
        GO
        CREATE FUNCTION dbo.fn_Inline (@Code VARCHAR(3))
        RETURNS TABLE
        AS
        RETURN (SELECT @Code AS Code);
        GO
        CREATE PROCEDURE dbo.usp_Caller
        AS
        BEGIN
            SELECT * FROM dbo.fn_Multi('abcdef');
            SELECT * FROM dbo.fn_Inline('abcdef');
            SELECT * FROM dbo.fn_Multi('abc');
            SELECT * FROM dbo.fn_Inline('abc');
        END
        """;

    protected override string Ddl => Sql;

    [Fact]
    public async Task LongLiteralArgument_IsReportedForInlineAndMultiStatementFunctionsAlike_AndTheFittingOnesStaySilent()
    {
        var report = await EngineAuthoritativeScan.ScanAsync(Sql);

        var callees = report.Find<TvfCallArgumentMismatchFinding>("TvfCallArgumentMismatchScanner")
            .Select(f => f.CalleeQualifiedName)
            .Order()
            .ToList();

        Assert.Equal(["dbo.fn_Inline", "dbo.fn_Multi"], callees);
    }
}
