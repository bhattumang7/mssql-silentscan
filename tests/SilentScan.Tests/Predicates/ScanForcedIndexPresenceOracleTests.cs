using SilentScan.Core.Catalog;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;
using SilentScan.Tests.Support;
using SilentScan.Verify.Oracle;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", "silentscan/verdict/scan-forced")]
public sealed class ScanForcedIndexPresenceOracleTests : OracleTestFixture
{
    private const string StaticDdl =
        "CREATE TABLE dbo.Conv (Id INT NOT NULL PRIMARY KEY CLUSTERED, KeyedV VARCHAR(20) NOT NULL, PlainV VARCHAR(20) NOT NULL, SecondV VARCHAR(20) NOT NULL);"
        + "CREATE NONCLUSTERED INDEX IX_Conv_KeyedV ON dbo.Conv(KeyedV);"
        + "CREATE NONCLUSTERED INDEX IX_Conv_Id_SecondV ON dbo.Conv(Id, SecondV);";

    protected override string DatabaseNameSeed => nameof(ScanForcedIndexPresenceOracleTests);

    protected override string Ddl => StaticDdl.Replace(";", ";\nGO\n", StringComparison.Ordinal);

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await ExecuteAsync(
            """
            INSERT INTO dbo.Conv (Id, KeyedV, PlainV, SecondV)
            SELECT TOP (50000) n, CAST(n AS VARCHAR(20)), CAST(n AS VARCHAR(20)), CAST(n AS VARCHAR(20))
            FROM (SELECT ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n FROM sys.all_objects a CROSS JOIN sys.all_objects b) x;

            UPDATE STATISTICS dbo.Conv WITH FULLSCAN;
            """);
    }

    private static IReadOnlyList<TypedPredicateFinding> Scan(string query)
    {
        var result = SqlScriptParser.ParseText("test.sql", $"{StaticDdl}\nGO\n{query}");
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));

        var catalog = CatalogBuilder.Build([result]);
        var lineage = LineageResolver.Resolve(catalog, [result]);
        return TypedPredicateExtractor.Extract(result, catalog, lineage).TypedFindings;
    }

    private async Task<bool> SeeksAsync(string query)
    {
        var plan = await PlanInSessionAsync(string.Empty, query);
        return ShowPlan.PhysicalOpsOnTable(plan, "Conv").Contains("Index Seek");
    }

    [Fact]
    public async Task IntLiteralAgainstVarcharColumnLeadingAnIndex_LosesTheSeek_ScannerReportsItAtHighConfidence()
    {
        const string Converted = "SELECT Id FROM dbo.Conv WHERE KeyedV = 123;";
        const string Matched = "SELECT Id FROM dbo.Conv WHERE KeyedV = '123';";

        Assert.True(await SeeksAsync(Matched));
        Assert.False(await SeeksAsync(Converted));
        var finding = Assert.Single(Scan(Converted), f => f.Verdict == Verdict.ScanForced);
        Assert.Equal(FindingConfidence.High, finding.Confidence);
    }

    private async Task<bool> ConvertsColumnAsync(string query)
    {
        var plan = await PlanInSessionAsync(string.Empty, query);
        return ConvertImplicitDetector.FindColumnConversions(plan.ToString()).Any(c => c.Table == "Conv");
    }

    [Theory]
    [InlineData("PlainV")]
    [InlineData("SecondV")]
    public async Task IntLiteralAgainstVarcharColumnWithoutALeadingIndex_PlanConvertsTheColumn_ScannerReportsItAtHighConfidence(string column)
    {
        var converted = $"SELECT Id FROM dbo.Conv WHERE {column} = 123;";
        var matched = $"SELECT Id FROM dbo.Conv WHERE {column} = '123';";

        Assert.False(await ConvertsColumnAsync(matched));
        Assert.True(await ConvertsColumnAsync(converted));
        Assert.DoesNotContain(Scan(matched), f => f.Verdict == Verdict.ScanForced);
        var finding = Assert.Single(Scan(converted), f => f.Verdict == Verdict.ScanForced);
        Assert.Equal(FindingConfidence.High, finding.Confidence);
    }
}
