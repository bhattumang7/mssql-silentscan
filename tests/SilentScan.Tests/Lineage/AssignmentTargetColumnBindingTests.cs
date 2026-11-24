using SilentScan.Core.Catalog;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;
using SilentScan.Core.Reporting;

namespace SilentScan.Tests.Lineage;

public sealed class AssignmentTargetColumnBindingTests
{
    private const string Tables = """
        CREATE TABLE dbo.Target (Id INT NOT NULL, Code VARCHAR(5) NOT NULL);
        GO
        CREATE TABLE dbo.Source (Id INT NOT NULL, Code NVARCHAR(10) NOT NULL);
        GO
        """;

    [Theory]
    [InlineData("UPDATE dbo.Target SET Code = s.Code FROM dbo.Target AS t JOIN dbo.Source AS s ON s.Id = t.Id;")]
    [InlineData("UPDATE t SET Code = s.Code FROM dbo.Target AS t JOIN dbo.Source AS s ON s.Id = t.Id;")]
    [InlineData("UPDATE dbo.Target SET Code = s.Code FROM dbo.Source AS s WHERE s.Id = dbo.Target.Id;")]
    [InlineData("MERGE dbo.Target AS t USING dbo.Source AS s ON s.Id = t.Id WHEN MATCHED THEN UPDATE SET Code = s.Code;")]
    public void UnqualifiedAssignmentTarget_BindsToTheModifiedTableEvenWhenTheSourceHasTheSameColumnName(string statement)
    {
        var result = SqlScriptParser.ParseText("assign.sql", Tables + Environment.NewLine + $"CREATE PROCEDURE dbo.p AS {statement}");
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));

        var report = ScanReportBuilder.BuildFromParseResults([result], CatalogBuilder.Build([result]));

        Assert.DoesNotContain(report.SkippedConstructs, s => s.Reason.Contains("ambiguous", StringComparison.Ordinal));
        Assert.Single(report.Find<WriteLossFinding>("TypedPredicateExtractor"));
    }
}
