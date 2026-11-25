using SilentScan.Core.Catalog;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;
using SilentScan.Core.Rules;
using SilentScan.Verify.Oracle;

namespace SilentScan.Tests.Predicates;

public sealed class NonSargableRescueTests
{
    private static bool Flags(RescueScenario scenario)
    {
        var ddl = scenario.Table.Replace("{t}", "dbo.t", StringComparison.Ordinal).Replace("; ", ";\nGO\n", StringComparison.Ordinal);
        var sql = ddl + $"\nGO\nCREATE PROCEDURE dbo.p AS SELECT 1 AS x FROM dbo.t WHERE {scenario.Predicate};";
        var result = SqlScriptParser.ParseText("t.sql", sql);
        Assert.False(result.HasErrors, scenario.Id + ": " + string.Join("; ", result.Errors.Select(e => e.Message)));
        var catalog = CatalogBuilder.Build([result]);
        return NonSargablePredicateScanner.Scan(result, catalog, LineageResolver.Resolve(catalog, [result])).Count > 0;
    }

    public static TheoryData<string> ScenarioIds() => [.. RescueScenarios.All.Select(s => s.Id)];

    [Theory]
    [MemberData(nameof(ScenarioIds))]
    public void Scanner_NeverFlagsAPredicateTheEngineSeeksOrAvoidsTheTableFor(string id)
    {
        var scenario = RescueScenarios.All.Single(s => s.Id == id);
        var engineScans = RescueMatrix.Instance.TryGet(id)!.Outcome.StartsWith(RescueMatrix.ScanOutcome, StringComparison.Ordinal);

        if (!engineScans)
        {
            Assert.False(Flags(scenario), $"{id} ({scenario.Predicate}) is flagged although the engine plans '{RescueMatrix.Instance.TryGet(id)!.Outcome}'.");
        }
    }

    [Fact]
    public void Matrix_ContainsBothRescuedAndUnrescuedShapesInEveryFamily()
    {
        var byFamily = RescueMatrix.Instance.Entries.GroupBy(e => e.Family).ToList();

        Assert.True(byFamily.Count >= 8);
        Assert.Contains(RescueMatrix.Instance.Entries, e => e.Outcome == RescueMatrix.NoTableAccessOutcome);
        Assert.Contains(RescueMatrix.Instance.Entries, e => e.Outcome == RescueMatrix.ScanOutcome);
        Assert.Contains(RescueMatrix.Instance.Entries, e => e.Outcome == RescueMatrix.SeekOutcome);
    }
}
