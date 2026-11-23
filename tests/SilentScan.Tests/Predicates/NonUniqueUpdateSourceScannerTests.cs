using SilentScan.Core.Catalog;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;

namespace SilentScan.Tests.Predicates;

public sealed class NonUniqueUpdateSourceScannerTests
{
    private static IReadOnlyList<NonUniqueUpdateSourceFinding> Scan(string sql, string extraDdl = "")
    {
        var ddl =
            "CREATE TABLE dbo.TargetT (Id INT NOT NULL PRIMARY KEY, Val INT NULL);" +
            "CREATE TABLE dbo.SourceNonUnique (TargetId INT NOT NULL, Val INT NOT NULL);" +
            "CREATE TABLE dbo.SourceUnique (TargetId INT NOT NULL UNIQUE, Val INT NOT NULL);" +
            "CREATE TABLE dbo.SourceCompositeUnique (TargetId INT NOT NULL, Cat INT NOT NULL, Val INT NOT NULL, CONSTRAINT UX_Composite UNIQUE (TargetId, Cat));" +
            extraDdl;
        var result = SqlScriptParser.ParseText("test.sql", $"{ddl}\nGO\n{sql}");
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));

        var catalog = CatalogBuilder.Build([result]);
        return NonUniqueUpdateSourceScanner.Scan(result, catalog);
    }

    [Fact]
    public void NonUniqueSource_SetClauseReadsFromIt_Fires()
    {
        var findings = Scan("UPDATE t SET t.Val = s.Val FROM dbo.TargetT t JOIN dbo.SourceNonUnique s ON t.Id = s.TargetId;");

        var finding = Assert.Single(findings);
        Assert.Equal("dbo.TargetT", finding.TargetTableQualifiedName);
        Assert.Equal("dbo.SourceNonUnique", finding.SourceTableQualifiedName);
        Assert.Equal(["TargetId"], finding.JoinColumnNames);
        Assert.Equal(["Val"], finding.SetColumnNames);
    }

    [Fact]
    public void UniqueIndexOnJoinColumn_NeverFires()
    {
        var findings = Scan("UPDATE t SET t.Val = s.Val FROM dbo.TargetT t JOIN dbo.SourceUnique s ON t.Id = s.TargetId;");

        Assert.Empty(findings);
    }

    [Theory]
    [InlineData("UPDATE t SET t.Val = s.Val FROM dbo.TargetT t JOIN (SELECT TargetId, MAX(Val) AS Val FROM dbo.SourceNonUnique GROUP BY TargetId) s ON t.Id = s.TargetId;")]
    [InlineData("UPDATE t SET t.Val = s.Val FROM dbo.TargetT t CROSS APPLY (SELECT TOP (1) Val FROM dbo.SourceNonUnique x WHERE x.TargetId = t.Id ORDER BY x.Val) s;")]
    [InlineData("UPDATE t SET t.Val = s.Val FROM dbo.TargetT t JOIN (SELECT DISTINCT TargetId, 1 AS Val FROM dbo.SourceNonUnique) s ON t.Id = s.TargetId;")]
    public void SourceCollapsedToOneRowPerKey_NeverFires(string sql)
    {
        Assert.Empty(Scan(sql));
    }

    [Fact]
    public void WhereClauseUnsatisfiable_NeverFires()
    {
        var findings = Scan(
            "UPDATE t SET t.Val = s.Val FROM dbo.TargetT t JOIN dbo.SourceNonUnique s ON t.Id = s.TargetId WHERE t.Id = 1 AND t.Id = 2;");

        Assert.Empty(findings);
    }

    [Fact]
    public void JoinOnClauseItselfUnsatisfiable_NeverFires()
    {
        var findings = Scan(
            "UPDATE t SET t.Val = s.Val FROM dbo.TargetT t JOIN dbo.SourceNonUnique s ON t.Id = s.TargetId AND s.TargetId = 1 AND s.TargetId = 2;");

        Assert.Empty(findings);
    }

    [Fact]
    public void UpdateThroughCteNamedLikeTheRealTargetTable_NeverFires()
    {

        var findings = Scan(
            "WITH TargetT AS (SELECT Id, Val FROM dbo.TargetT) " +
            "UPDATE t SET t.Val = s.Val FROM TargetT t JOIN dbo.SourceNonUnique s ON t.Id = s.TargetId;");

        Assert.Empty(findings);
    }

    [Fact]
    public void ExactCompositeUniqueMatch_NeverFires()
    {
        var findings = Scan(
            "UPDATE t SET t.Val = s.Val FROM dbo.TargetT t JOIN dbo.SourceCompositeUnique s ON t.Id = s.TargetId AND t.Id = s.Cat;");

        Assert.Empty(findings);
    }

    [Theory]
    [InlineData("ON t.Id = s.TargetId AND s.Cat = 1;", false)]
    [InlineData("ON t.Id = s.TargetId WHERE s.Cat = 1;", false)]
    [InlineData("ON t.Id = s.TargetId WHERE 1 = s.Cat;", false)]
    [InlineData("ON t.Id = s.TargetId WHERE s.Cat > 1;", true)]
    [InlineData("ON t.Id = s.TargetId WHERE s.Cat = 1 OR s.Cat = 2;", true)]
    [InlineData("ON t.Id = s.TargetId;", true)]
    public void CompositeUniqueSource_FiresUnlessTheRemainingKeyColumnIsPinned(string tail, bool fires)
    {
        var findings = Scan($"UPDATE t SET t.Val = s.Val FROM dbo.TargetT t JOIN dbo.SourceCompositeUnique s {tail}");

        Assert.Equal(fires, findings.Count == 1);
    }

    [Fact]
    public void CompositeUniqueSuperset_JoinOnSubsetOnly_StillFires()
    {

        var findings = Scan(
            "UPDATE t SET t.Val = s.Val FROM dbo.TargetT t JOIN dbo.SourceCompositeUnique s ON t.Id = s.TargetId;");

        Assert.Single(findings);
    }

    [Fact]
    public void SubsetOfCompositeJoin_UniqueOnSubsetAlone_NeverFires()
    {

        var findings = Scan(
            "UPDATE t SET t.Val = s.Val FROM dbo.TargetT t JOIN dbo.SourceUnique s ON t.Id = s.TargetId AND t.Val = s.Val;");

        Assert.Empty(findings);
    }

    [Fact]
    public void NonUniqueSource_SetClauseNeverReadsFromIt_NeverFires()
    {
        var findings = Scan("UPDATE t SET t.Val = 1 FROM dbo.TargetT t JOIN dbo.SourceNonUnique s ON t.Id = s.TargetId;");

        Assert.Empty(findings);
    }

    [Fact]
    public void NoFromClause_SimpleUpdate_NeverFires()
    {
        var findings = Scan("UPDATE dbo.TargetT SET Val = 1 WHERE Id = 1;");

        Assert.Empty(findings);
    }

    [Fact]
    public void SelfJoin_NonUniqueSourceSide_Fires()
    {

        var findings = Scan(
            "UPDATE t1 SET t1.Val = t2.Val FROM dbo.TargetT t1 JOIN dbo.TargetT t2 ON t1.Id = t2.Val;");

        var finding = Assert.Single(findings);
        Assert.Equal("dbo.TargetT", finding.TargetTableQualifiedName);
        Assert.Equal("dbo.TargetT", finding.SourceTableQualifiedName);
    }

    [Fact]
    public void SetValueIsExpressionReferencingSource_StillFires()
    {
        var findings = Scan("UPDATE t SET t.Val = s.Val + 1 FROM dbo.TargetT t JOIN dbo.SourceNonUnique s ON t.Id = s.TargetId;");

        Assert.Single(findings);
    }

    [Fact]
    public void FilteredUniqueIndex_TreatedAsNotProvablyUnique_Fires()
    {
        var findings = Scan(
            "UPDATE t SET t.Val = s.Val FROM dbo.TargetT t JOIN dbo.SourceFiltered s ON t.Id = s.TargetId;",
            extraDdl: "CREATE TABLE dbo.SourceFiltered (TargetId INT NOT NULL, Val INT NOT NULL); " +
                      "CREATE UNIQUE INDEX UX_Filtered ON dbo.SourceFiltered(TargetId) WHERE Val > 0;");

        Assert.Single(findings);
    }

    [Fact]
    public void IndirectJoinNotTouchingTarget_NeverFires()
    {

        var findings = Scan(
            "UPDATE t SET t.Val = b.Val FROM dbo.TargetT t JOIN dbo.SourceUnique a ON t.Id = a.TargetId JOIN dbo.SourceNonUnique b ON a.TargetId = b.TargetId;",
            extraDdl: "");

        Assert.Empty(findings);
    }
}
