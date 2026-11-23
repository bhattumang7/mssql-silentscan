using SilentScan.Core.Catalog;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;

namespace SilentScan.Tests.Predicates;

public sealed class NotInNullableSubqueryScannerTests
{
    private static IReadOnlyList<NotInNullableSubqueryFinding> Scan(string sql, string extraDdl = "")
    {
        var ddl =
            "CREATE TABLE dbo.Parent (Id INT NOT NULL);" +
            "CREATE TABLE dbo.ChildNullable (RefId INT NULL, INDEX IX_ChildNullable_RefId (RefId));" +
            "CREATE TABLE dbo.ChildNotNull (RefId INT NOT NULL);" +
            extraDdl;
        var result = SqlScriptParser.ParseText("test.sql", $"{ddl}\nGO\n{sql}");
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));

        var catalog = CatalogBuilder.Build([result]);
        return NotInNullableSubqueryScanner.Scan(result, catalog);
    }

    [Theory]
    [InlineData("SELECT Id FROM dbo.Parent WHERE Id NOT IN (SELECT n.RefId FROM dbo.ChildNullable n JOIN dbo.ChildNotNull c ON c.RefId = n.RefId);", false)]
    [InlineData("SELECT Id FROM dbo.Parent WHERE Id NOT IN (SELECT n.RefId FROM dbo.ChildNullable n INNER JOIN dbo.ChildNotNull c ON n.RefId = c.RefId AND c.RefId > 0);", false)]
    [InlineData("SELECT Id FROM dbo.Parent WHERE Id NOT IN (SELECT n.RefId FROM dbo.ChildNullable n LEFT JOIN dbo.ChildNotNull c ON c.RefId = n.RefId);", true)]
    [InlineData("SELECT Id FROM dbo.Parent WHERE Id NOT IN (SELECT n.RefId FROM dbo.ChildNullable n JOIN dbo.ChildNotNull c ON c.RefId > 0);", true)]
    public void InnerJoinConditionOnTheSubqueryColumn_RemovesTheNullsLikeAWhereFilter(string sql, bool fires)
    {
        Assert.Equal(fires, Scan(sql).Count == 1);
    }

    [Fact]
    public void NullableSubqueryColumn_Fires()
    {
        var findings = Scan("SELECT Id FROM dbo.Parent WHERE Id NOT IN (SELECT RefId FROM dbo.ChildNullable);");

        var finding = Assert.Single(findings);
        Assert.Equal("dbo.ChildNullable", finding.SubqueryTableQualifiedName);
        Assert.Equal("RefId", finding.SubqueryColumnName);
        Assert.Equal("Id", finding.OuterColumnName);
        Assert.True(finding.SubqueryColumnIndexed);
    }

    [Theory]
    [InlineData("SELECT Id FROM dbo.Parent WHERE Id NOT IN (SELECT c.RefId FROM dbo.ChildNotNull n LEFT JOIN dbo.ChildNotNull c ON c.RefId = n.RefId);")]
    [InlineData("SELECT Id FROM dbo.Parent WHERE Id NOT IN (SELECT c.RefId FROM dbo.ChildNotNull c RIGHT JOIN dbo.ChildNotNull n ON c.RefId = n.RefId);")]
    public void NotNullColumnOnTheNullSuppliedSideOfAnOuterJoinInTheSubquery_Fires(string sql)
    {
        var finding = Assert.Single(Scan(sql));

        Assert.Equal("dbo.ChildNotNull", finding.SubqueryTableQualifiedName);
        Assert.Equal("RefId", finding.SubqueryColumnName);
    }

    [Theory]
    [InlineData("SELECT Id FROM dbo.Parent WHERE Id NOT IN (SELECT n.RefId FROM dbo.ChildNotNull n LEFT JOIN dbo.ChildNotNull c ON c.RefId = n.RefId);")]
    [InlineData("SELECT Id FROM dbo.Parent WHERE Id NOT IN (SELECT c.RefId FROM dbo.ChildNotNull n INNER JOIN dbo.ChildNotNull c ON c.RefId = n.RefId);")]
    [InlineData("SELECT Id FROM dbo.Parent WHERE Id NOT IN (SELECT c.RefId FROM dbo.ChildNotNull n LEFT JOIN dbo.ChildNotNull c ON c.RefId = n.RefId WHERE c.RefId IS NOT NULL);")]
    public void NotNullColumnOnThePreservedSideOrGuardedOrInAnInnerJoin_NeverFires(string sql)
    {
        Assert.Empty(Scan(sql));
    }

    [Fact]
    public void OuterWhereClauseUnsatisfiable_NeverFires()
    {
        var findings = Scan("SELECT Id FROM dbo.Parent WHERE Id NOT IN (SELECT RefId FROM dbo.ChildNullable) AND Id = 1 AND Id = 2;");

        Assert.Empty(findings);
    }

    [Fact]
    public void NotNullSubqueryColumn_NeverFires()
    {
        var findings = Scan("SELECT Id FROM dbo.Parent WHERE Id NOT IN (SELECT RefId FROM dbo.ChildNotNull);");

        Assert.Empty(findings);
    }

    [Fact]
    public void SubquerySourceIsACteSharingANameWithANullableRealTable_NeverFires()
    {

        var findings = Scan(
            "WITH ChildNullable AS (SELECT RefId FROM dbo.ChildNotNull) " +
            "SELECT Id FROM dbo.Parent WHERE Id NOT IN (SELECT RefId FROM ChildNullable);");

        Assert.Empty(findings);
    }

    [Fact]
    public void PlainIn_NotNegated_NeverFires()
    {
        var findings = Scan("SELECT Id FROM dbo.Parent WHERE Id IN (SELECT RefId FROM dbo.ChildNullable);");

        Assert.Empty(findings);
    }

    [Fact]
    public void LiteralValueList_NeverFires()
    {
        var findings = Scan("SELECT Id FROM dbo.Parent WHERE Id NOT IN (1, 2, 3);");

        Assert.Empty(findings);
    }

    [Fact]
    public void SubqueryWithDefensiveNotNullFilter_NeverFires()
    {
        var findings = Scan("SELECT Id FROM dbo.Parent WHERE Id NOT IN (SELECT RefId FROM dbo.ChildNullable WHERE RefId IS NOT NULL);");

        Assert.Empty(findings);
    }

    [Theory]
    [InlineData("RefId > 0")]
    [InlineData("RefId = 5")]
    [InlineData("RefId <> 5")]
    [InlineData("RefId BETWEEN 1 AND 9")]
    [InlineData("RefId IN (1, 2)")]
    [InlineData("0 < RefId")]
    public void SubqueryFilterThatRejectsNull_NeverFires(string filter)
    {
        var findings = Scan($"SELECT Id FROM dbo.Parent WHERE Id NOT IN (SELECT RefId FROM dbo.ChildNullable WHERE {filter});");

        Assert.Empty(findings);
    }

    [Fact]
    public void SubqueryFilterThatDoesNotRejectNull_StillFires()
    {
        var findings = Scan("SELECT Id FROM dbo.Parent WHERE Id NOT IN (SELECT RefId FROM dbo.ChildNullable WHERE RefId IS NULL OR RefId > 0);");

        Assert.Single(findings);
    }

    [Fact]
    public void SubqueryFilterWithNullSafeComparison_StillFires()
    {
        var findings = Scan("SELECT Id FROM dbo.Parent WHERE Id NOT IN (SELECT RefId FROM dbo.ChildNullable WHERE RefId IS NOT DISTINCT FROM 5);");

        Assert.Single(findings);
    }

    [Fact]
    public void SubqueryNotNullFilterOnlyReachableThroughOr_StillFires()
    {

        var findings = Scan(
            "SELECT Id FROM dbo.Parent WHERE Id NOT IN (SELECT RefId FROM dbo.ChildNullable WHERE RefId IS NOT NULL OR RefId = 1);");

        Assert.Single(findings);
    }

    [Fact]
    public void SubqueryNotNullFilterOnDifferentColumn_StillFires()
    {
        var findings = Scan(
            "SELECT Id FROM dbo.Parent WHERE Id NOT IN (SELECT RefId FROM dbo.ChildNullable WHERE Id2 IS NOT NULL);",
            extraDdl: "ALTER TABLE dbo.ChildNullable ADD Id2 INT NULL;");

        Assert.Single(findings);
    }

    [Fact]
    public void SubqueryProjectsExpression_NeverFires()
    {
        var findings = Scan("SELECT Id FROM dbo.Parent WHERE Id NOT IN (SELECT ISNULL(RefId, 0) FROM dbo.ChildNullable);");

        Assert.Empty(findings);
    }

    [Fact]
    public void SubqueryProjectsMultipleColumns_NeverFires()
    {

        var findings = Scan("SELECT Id FROM dbo.Parent WHERE Id NOT IN (SELECT RefId, RefId FROM dbo.ChildNullable);");

        Assert.Empty(findings);
    }

    [Fact]
    public void NotExists_NeverFires()
    {
        var findings = Scan(
            "SELECT Id FROM dbo.Parent p WHERE NOT EXISTS (SELECT 1 FROM dbo.ChildNullable c WHERE c.RefId = p.Id);");

        Assert.Empty(findings);
    }

    [Fact]
    public void InPredicateInsideOrBranch_NeverFires()
    {
        var findings = Scan(
            "SELECT Id FROM dbo.Parent WHERE Id = 1 OR Id NOT IN (SELECT RefId FROM dbo.ChildNullable);");

        Assert.Empty(findings);
    }

    [Fact]
    public void NullableSubqueryColumn_InJoinOnClause_Fires()
    {
        var findings = Scan(
            "SELECT p.Id FROM dbo.Parent p JOIN dbo.ChildNotNull c ON p.Id NOT IN (SELECT RefId FROM dbo.ChildNullable) WHERE c.RefId = p.Id;");

        Assert.Single(findings);
    }

    [Fact]
    public void NullableSubqueryColumn_InHavingClause_Fires()
    {
        var findings = Scan("SELECT Id FROM dbo.Parent GROUP BY Id HAVING Id NOT IN (SELECT RefId FROM dbo.ChildNullable);");

        Assert.Single(findings);
    }

    [Fact]
    public void UpdateStatement_SameShapeInWhereClause_Fires()
    {
        var findings = Scan("UPDATE dbo.Parent SET Id = Id WHERE Id NOT IN (SELECT RefId FROM dbo.ChildNullable);");

        Assert.Single(findings);
    }

    [Fact]
    public void OuterSideIsExpression_StillFiresWithNullOuterColumnName()
    {
        var findings = Scan("SELECT Id FROM dbo.Parent WHERE (Id + 1) NOT IN (SELECT RefId FROM dbo.ChildNullable);");

        var finding = Assert.Single(findings);
        Assert.Null(finding.OuterColumnName);
    }

    [Fact]
    public void SubqueryColumnNotOnItsOwnFromTable_SilentlyCorrelatesToOuterNullableColumn_Fires()
    {
        var findings = Scan(
            "SELECT * FROM dbo.CorrOuter WHERE Id NOT IN (SELECT RefId FROM dbo.CorrInner);",
            extraDdl: "CREATE TABLE dbo.CorrOuter (Id INT NOT NULL, RefId INT NULL);"
                + "CREATE TABLE dbo.CorrInner (OtherCol INT NOT NULL);");

        var finding = Assert.Single(findings);
        Assert.Equal("dbo.CorrOuter", finding.SubqueryTableQualifiedName);
        Assert.Equal("RefId", finding.SubqueryColumnName);
        Assert.Equal("Id", finding.OuterColumnName);
    }
}
