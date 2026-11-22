using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.ScalarUdfProjectionInvocationRuleId)]
[Trait("Rule", FindingRuleIds.ScalarUdfPredicateInvocationRuleId)]
public sealed class ScalarUdfInliningStatementThresholdOracleTests : OracleTestFixture
{
    private const string NotParallelizable = "TSQLUserDefinedFunctionsNotParallelizable";

    private const string NotInlinedMarker = "<UserDefinedFunction FunctionName=";

    protected override string DatabaseNameSeed => nameof(ScalarUdfInliningStatementThresholdOracleTests);

    protected override string Ddl => """
        ALTER DATABASE CURRENT SET COMPATIBILITY_LEVEL = 160;
        GO
        CREATE FUNCTION dbo.F(@x INT) RETURNS INT AS BEGIN RETURN @x + 1; END;
        GO
        CREATE FUNCTION dbo.G(@x INT) RETURNS INT AS BEGIN RETURN @x + 2; END;
        GO
        """;

    private static string SelectOf(IEnumerable<string> columns) => "SELECT " + string.Join(", ", columns) + ";";

    private static IEnumerable<string> Calls(string function, int count, int offset = 0) =>
        Enumerable.Range(offset, count).Select(i => $"dbo.{function}({i}) AS {function}{i}");

    [Fact]
    public async Task OneHundredInlineableReferencesInOneStatement_AreInlined()
    {
        var planXml = await CaptureActualPlanAsync(SelectOf(Calls("F", 100)));

        Assert.DoesNotContain(NotInlinedMarker, planXml, StringComparison.Ordinal);
        Assert.DoesNotContain(NotParallelizable, planXml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OneHundredAndOneInlineableReferencesInOneStatement_AreNotInlined_AndForceASerialPlan()
    {
        var planXml = await CaptureActualPlanAsync(SelectOf(Calls("F", 101)));

        Assert.Contains(NotInlinedMarker, planXml, StringComparison.Ordinal);
        Assert.Contains(NotParallelizable, planXml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReferencesAcrossDifferentFunctionsCountTowardTheSameStatementLimit()
    {
        var planXml = await CaptureActualPlanAsync(SelectOf(Calls("F", 51).Concat(Calls("G", 50))));

        Assert.Contains(NotInlinedMarker, planXml, StringComparison.Ordinal);
        Assert.Contains(NotParallelizable, planXml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NestedReferencesCountTowardTheStatementLimit()
    {
        static IEnumerable<string> Nested(int count) =>
            Enumerable.Range(0, count).Select(i => $"dbo.F(dbo.G({i})) AS N{i}");

        var withinLimit = await CaptureActualPlanAsync(SelectOf(Nested(50)));
        var overLimit = await CaptureActualPlanAsync(SelectOf(Nested(60)));

        Assert.DoesNotContain(NotInlinedMarker, withinLimit, StringComparison.Ordinal);
        Assert.Contains(NotInlinedMarker, overLimit, StringComparison.Ordinal);
    }
}
