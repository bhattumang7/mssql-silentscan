using SilentScan.Core.Catalog;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;
using SilentScan.Core.Reporting;

namespace SilentScan.Tests.Lineage;

public sealed class DataModificationTargetScopeTests
{
    private const string Tables = """
        CREATE TABLE dbo.Target (Id INT NOT NULL PRIMARY KEY, Code VARCHAR(10) NOT NULL, Note VARCHAR(10) NULL, INDEX IX_Target_Code (Code));
        GO
        CREATE TABLE dbo.Feed (Id INT NOT NULL);
        GO
        """;

    private static ScanReport Scan(string procedure)
    {
        var result = SqlScriptParser.ParseText("target.sql", Tables + Environment.NewLine + procedure);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        return ScanReportBuilder.BuildFromParseResults([result], CatalogBuilder.Build([result]));
    }

    private static List<TypedPredicateFinding> CodeFindings(ScanReport report) =>
        [.. report.Find<TypedPredicateFinding>("TypedPredicateExtractor").Where(f => f.ColumnName == "Code")];

    [Theory]
    [InlineData("UPDATE dbo.Target SET dbo.Target.Note = 'x' FROM dbo.Feed AS f WHERE dbo.Target.Id = f.Id AND dbo.Target.Code = @c;")]
    [InlineData("DELETE dbo.Target FROM dbo.Feed AS f WHERE dbo.Target.Id = f.Id AND dbo.Target.Code = @c;")]
    public void TargetAbsentFromFrom_IsBoundAsItsOwnInstance(string statement)
    {
        var report = Scan($"CREATE PROCEDURE dbo.p @c INT AS {statement}");

        Assert.DoesNotContain(report.SkippedConstructs, s => s.Reason.Contains("'Target'", StringComparison.Ordinal));
        var finding = Assert.Single(CodeFindings(report));
        Assert.Equal("dbo.Target", finding.TableQualifiedName);
    }

    [Theory]
    [InlineData("UPDATE t SET Note = 'x' FROM dbo.Target AS t WHERE t.Code = @c;")]
    [InlineData("UPDATE dbo.Target SET Note = 'x' FROM dbo.Target AS t JOIN dbo.Feed AS f ON f.Id = t.Id WHERE t.Code = @c;")]
    [InlineData("UPDATE dbo.Target SET Note = 'x' FROM dbo.Target JOIN dbo.Feed AS f ON f.Id = Target.Id WHERE Target.Code = @c;")]
    public void TargetNamedInFrom_BindsToThatEntryWithoutASecondInstance(string statement)
    {
        var report = Scan($"CREATE PROCEDURE dbo.p @c INT AS {statement}");

        Assert.DoesNotContain(report.SkippedConstructs, s => s.ConstructKind == "FROM alias");
        Assert.Single(CodeFindings(report));
    }

    [Fact]
    public void TriggerUpdateOfItsOwnTableFromInserted_BindsTheTargetAsItsOwnInstanceNotAsInserted()
    {
        var report = Scan("""
            CREATE TRIGGER dbo.trTarget ON dbo.Target AFTER INSERT AS
            UPDATE dbo.Target SET dbo.Target.Note = 'x' FROM inserted WHERE dbo.Target.Id = inserted.Id AND dbo.Target.Code = inserted.Id;
            """);

        Assert.DoesNotContain(report.SkippedConstructs, s => s.Reason.Contains("'Target'", StringComparison.Ordinal));
        var finding = Assert.Single(CodeFindings(report));
        Assert.Equal("dbo.Target", finding.TableQualifiedName);
    }
}
