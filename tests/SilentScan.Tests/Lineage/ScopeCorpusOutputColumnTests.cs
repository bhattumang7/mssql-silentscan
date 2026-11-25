using SilentScan.Core.Catalog;
using SilentScan.Core.Lineage;
using SilentScan.Core.Parsing;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Lineage;

public sealed class ScopeCorpusOutputColumnTests
{
    private const string OurPrefix = "x_";

    public static TheoryData<string> OutputColumnScenarioIds() =>
        [.. ScopeCorpus.Load().Where(e => e.Kind == "OutputColumns").Select(e => e.Id)];

    [Fact]
    public void Corpus_HasTheOutputColumnScenarios()
    {
        Assert.True(ScopeCorpus.Load().Count(e => e.Kind == "OutputColumns") >= 12);
    }

    [Theory]
    [MemberData(nameof(OutputColumnScenarioIds))]
    public void OurLineage_BindsEveryOutputColumnToTheSameSourceAsTheEngine(string id)
    {
        var entry = ScopeCorpus.Get(id);

        var script = entry.Setup.Replace("{P}", OurPrefix, StringComparison.Ordinal)
            + Environment.NewLine + "GO" + Environment.NewLine
            + $"CREATE VIEW dbo.{OurPrefix}probe AS {ViewBody(entry.Body.Replace("{P}", OurPrefix, StringComparison.Ordinal))};";
        var result = SqlScriptParser.ParseText("scope.sql", script);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));

        var lineage = LineageResolver.Resolve(CatalogBuilder.Build([result]), [result]);
        var relation = lineage.Find($"dbo.{OurPrefix}probe");

        Assert.NotNull(relation);
        var ours = string.Join(";", relation.Columns.Select(c => c.Name + "=" + SourceOf(c.Provenance)));
        Assert.Equal(entry.Observed, ours.Replace(OurPrefix, "P_", StringComparison.OrdinalIgnoreCase), ignoreCase: true);
    }

    private static string ViewBody(string select) =>
        select.Contains("ORDER BY", StringComparison.Ordinal) && !select.Contains("TOP", StringComparison.Ordinal) && !select.Contains("OVER (", StringComparison.Ordinal) ? select + " OFFSET 0 ROWS" : select;

    private static string SourceOf(ColumnProvenance provenance) => provenance switch
    {
        ColumnProvenance.BaseColumn b => b.TableQualifiedName + "." + b.ColumnName,
        _ => "?",
    };
}
