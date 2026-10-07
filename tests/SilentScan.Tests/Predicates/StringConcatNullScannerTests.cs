using SilentScan.Core.Catalog;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;

namespace SilentScan.Tests.Predicates;

public sealed class StringConcatNullScannerTests
{
    private static IReadOnlyList<StringConcatNullFinding> Scan(string sql)
    {
        var ddl = """
            CREATE TABLE dbo.Person (
                Id INT NOT NULL PRIMARY KEY,
                FirstName VARCHAR(50) NOT NULL,
                MiddleName VARCHAR(50) NULL,
                LastName VARCHAR(50) NOT NULL,
                Age INT NULL
            );
            CREATE TABLE dbo.Other (Id INT NOT NULL PRIMARY KEY, Label VARCHAR(50) NULL);
            CREATE TABLE dbo.Person2 (Id INT NOT NULL PRIMARY KEY, LastName2 VARCHAR(50) NOT NULL);
            """;
        var result = SqlScriptParser.ParseText("test.sql", $"{ddl}\nGO\n{sql}");
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));

        var catalog = CatalogBuilder.Build([result]);
        return StringConcatNullScanner.Scan(result, catalog);
    }

    [Theory]
    [InlineData("SELECT p.FirstName + p.MiddleName FROM dbo.Person p INNER JOIN dbo.Other o ON o.Label = p.MiddleName;")]
    [InlineData("SELECT p.FirstName + p.MiddleName FROM dbo.Person p INNER JOIN dbo.Other o ON o.Label = p.MiddleName LEFT JOIN dbo.Person2 q ON q.Id = p.Id;")]
    [InlineData("SELECT FirstName + MiddleName FROM dbo.Person WHERE MiddleName BETWEEN 'a' AND 'z';")]
    [InlineData("SELECT FirstName + MiddleName FROM dbo.Person WHERE MiddleName NOT BETWEEN 'a' AND 'z';")]
    [InlineData("SELECT FirstName + MiddleName FROM dbo.Person WHERE MiddleName NOT LIKE 'a%';")]
    [InlineData("SELECT FirstName + MiddleName FROM dbo.Person WHERE MiddleName NOT IN ('a', 'b');")]
    [InlineData("SELECT FirstName + MiddleName FROM dbo.Person GROUP BY FirstName, MiddleName HAVING MiddleName > '';")]
    public void ColumnWhoseNullsAreRemovedByAFilter_NeverFires(string sql)
    {
        Assert.Empty(Scan(sql));
    }

    [Theory]
    [InlineData("SELECT FirstName + MiddleName FROM dbo.Person WHERE MiddleName IS DISTINCT FROM 'x';")]
    [InlineData("SELECT FirstName + MiddleName FROM dbo.Person WHERE 'q' NOT BETWEEN MiddleName AND 'b';")]
    [InlineData("SELECT FirstName + MiddleName FROM dbo.Person WHERE MiddleName NOT IN (SELECT Label FROM dbo.Other);")]
    [InlineData("SELECT p.FirstName + p.MiddleName FROM dbo.Person p LEFT JOIN dbo.Other o ON o.Label = p.MiddleName;")]
    public void FilterThatKeepsNullRows_StillFires(string sql)
    {
        Assert.Equal("MiddleName", Assert.Single(Scan(sql)).ColumnName);
    }

    [Fact]
    public void NullableColumnConcatenatedWithLiteral_Fires()
    {
        var findings = Scan("SELECT FirstName + ' ' + MiddleName FROM dbo.Person;");

        var finding = Assert.Single(findings);
        Assert.Equal("dbo.Person", finding.TableQualifiedName);
        Assert.Equal("MiddleName", finding.ColumnName);
    }

    [Theory]
    [InlineData("SELECT p.FirstName + ' ' + o.LastName2 FROM dbo.Person p LEFT JOIN dbo.Person2 o ON o.Id = p.Id;")]
    [InlineData("SELECT p.FirstName + ' ' + o.LastName2 FROM dbo.Person2 o RIGHT JOIN dbo.Person p ON o.Id = p.Id;")]
    public void NotNullColumnOnTheNullSuppliedSideOfAnOuterJoin_Fires(string sql)
    {
        var findings = Scan(sql);

        var finding = Assert.Single(findings);
        Assert.Equal("dbo.Person2", finding.TableQualifiedName);
        Assert.Equal("LastName2", finding.ColumnName);
    }

    [Theory]
    [InlineData("SELECT p.FirstName + ' ' + o.LastName2 FROM dbo.Person p INNER JOIN dbo.Person2 o ON o.Id = p.Id;")]
    [InlineData("SELECT o.LastName2 + ' ' + o.LastName2 FROM dbo.Person2 o LEFT JOIN dbo.Person p ON o.Id = p.Id;")]
    [InlineData("SELECT p.FirstName + ' ' + o.LastName2 FROM dbo.Person p LEFT JOIN dbo.Person2 o ON o.Id = p.Id WHERE o.LastName2 = 'X';")]
    public void NotNullColumnOnThePreservedSideOrInAnInnerJoin_NeverFires(string sql)
    {
        Assert.Empty(Scan(sql));
    }

    [Theory]
    [InlineData("SELECT CASE WHEN MiddleName IS NULL THEN FirstName ELSE FirstName + ' ' + MiddleName END FROM dbo.Person;")]
    [InlineData("SELECT CASE WHEN MiddleName IS NOT NULL THEN FirstName + ' ' + MiddleName ELSE FirstName END FROM dbo.Person;")]
    [InlineData("SELECT CASE WHEN MiddleName <> '' THEN FirstName + ' ' + MiddleName ELSE FirstName END FROM dbo.Person;")]
    [InlineData("SELECT CASE WHEN Age > 1 THEN FirstName WHEN MiddleName IS NULL THEN LastName ELSE FirstName + MiddleName END FROM dbo.Person;")]
    [InlineData("SELECT CASE WHEN MiddleName IS NOT NULL AND Age > 1 THEN FirstName + MiddleName END FROM dbo.Person;")]
    [InlineData("SELECT CASE WHEN NOT (MiddleName IS NULL) THEN FirstName + MiddleName END FROM dbo.Person;")]
    public void ConcatenationInsideCaseBranchThatProvesTheColumnNotNull_NeverFires(string sql)
    {
        Assert.Empty(Scan(sql));
    }

    [Theory]
    [InlineData("SELECT CASE WHEN Age IS NULL THEN FirstName ELSE FirstName + ' ' + MiddleName END FROM dbo.Person;")]
    [InlineData("SELECT CASE WHEN MiddleName IS NULL THEN FirstName + MiddleName ELSE FirstName END FROM dbo.Person;")]
    [InlineData("SELECT CASE WHEN MiddleName IS NOT NULL THEN FirstName ELSE FirstName + MiddleName END FROM dbo.Person;")]
    [InlineData("SELECT CASE WHEN MiddleName IS NOT NULL OR Age > 1 THEN FirstName + MiddleName END FROM dbo.Person;")]
    public void ConcatenationInsideCaseBranchThatDoesNotProveTheColumnNotNull_StillFires(string sql)
    {
        var finding = Assert.Single(Scan(sql));
        Assert.Equal("MiddleName", finding.ColumnName);
    }

    [Fact]
    public void ConcatenationFeedingForXmlPathAggregation_NeverFires()
    {
        Assert.Empty(Scan("SELECT ',' + MiddleName FROM dbo.Person FOR XML PATH('');"));
        Assert.Empty(Scan("SELECT (SELECT ',' + MiddleName AS [data()] FROM dbo.Person FOR XML PATH('')) AS Joined;"));
    }

    [Fact]
    public void ConcatenationInPlainSelectBesideForXmlSubquery_StillFires()
    {
        var findings = Scan("SELECT FirstName + MiddleName, (SELECT ',' + Label FROM dbo.Other FOR XML PATH('')) FROM dbo.Person;");

        var finding = Assert.Single(findings);
        Assert.Equal("MiddleName", finding.ColumnName);
    }

    [Fact]
    public void ThreeLeafChain_ReportsOnceForWholeChain()
    {

        var findings = Scan("SELECT FirstName + ' ' + MiddleName + ' ' + LastName FROM dbo.Person;");

        Assert.Single(findings);
    }

    [Fact]
    public void NonNullableColumnsOnly_NeverFires()
    {
        var findings = Scan("SELECT FirstName + ' ' + LastName FROM dbo.Person;");

        Assert.Empty(findings);
    }

    [Fact]
    public void CteSharesNameWithRealTable_ResolvesThroughCteToRealColumn_Fires()
    {

        var findings = Scan(
            "WITH Person AS (SELECT FirstName, MiddleName FROM dbo.Person WHERE MiddleName IS NOT NULL) " +
            "SELECT FirstName + ' ' + MiddleName FROM Person;");

        var finding = Assert.Single(findings);
        Assert.Equal("dbo.Person", finding.TableQualifiedName);
        Assert.Equal("MiddleName", finding.ColumnName);
    }

    [Fact]
    public void CteSharesNameWithRealTable_ButUnderlyingColumnNotNullable_NeverFires()
    {

        var findings = Scan(
            "WITH Person AS (SELECT FirstName, LastName FROM dbo.Person) " +
            "SELECT FirstName + ' ' + LastName FROM Person;");

        Assert.Empty(findings);
    }

    [Fact]
    public void NullableOperandGuardedByIsNull_NeverFires()
    {
        var findings = Scan("SELECT FirstName + ' ' + ISNULL(MiddleName, '') FROM dbo.Person;");

        Assert.Empty(findings);
    }

    [Fact]
    public void NullableOperandGuardedByCoalesce_NeverFires()
    {
        var findings = Scan("SELECT FirstName + ' ' + COALESCE(MiddleName, '') FROM dbo.Person;");

        Assert.Empty(findings);
    }

    [Fact]
    public void WholeChainGuardedByOuterIsNull_NeverFires()
    {
        var findings = Scan("SELECT ISNULL(FirstName + ' ' + MiddleName, '') FROM dbo.Person;");

        Assert.Empty(findings);
    }

    [Fact]
    public void CoalesceExpression_NotAFunctionCall_StillGuardsBothChains()
    {

        var findings = Scan(
            "SELECT LTRIM(RTRIM(COALESCE(FirstName + ' ' + MiddleName + ' ' + LastName, FirstName + ' ' + LastName))) FROM dbo.Person;");

        Assert.Empty(findings);
    }

    [Fact]
    public void NumericAddition_NeverFires()
    {

        var findings = Scan("SELECT Age + 1 FROM dbo.Person;");

        Assert.Empty(findings);
    }

    [Fact]
    public void UnresolvableOperand_DeclinesRatherThanGuesses()
    {

        var findings = Scan("DECLARE @suffix VARCHAR(10) = 'x'; SELECT FirstName + @suffix + MiddleName FROM dbo.Person;");

        Assert.Empty(findings);
    }

    [Fact]
    public void UpdateSetClause_NullableColumnConcatenated_Fires()
    {
        var findings = Scan("UPDATE dbo.Person SET FirstName = FirstName + ' ' + MiddleName;");

        var finding = Assert.Single(findings);
        Assert.Equal("MiddleName", finding.ColumnName);
    }

    [Fact]
    public void FromClauseCasingDiffersFromDdl_ReportsFromClauseCasing()
    {

        var findings = Scan("SELECT FirstName + ' ' + MiddleName FROM DBO.PERSON;");

        var finding = Assert.Single(findings);
        Assert.Equal("DBO.PERSON", finding.TableQualifiedName);
    }

    [Fact]
    public void QualifiedByAlias_Fires()
    {
        var findings = Scan("SELECT p.FirstName + ' ' + p.MiddleName FROM dbo.Person p;");

        Assert.Single(findings);
    }

    [Fact]
    public void ConcatNullYieldsNullOff_SuppressesFinding()
    {
        var findings = Scan("SET CONCAT_NULL_YIELDS_NULL OFF;\nSELECT FirstName + ' ' + MiddleName FROM dbo.Person;");

        Assert.Empty(findings);
    }

    [Fact]
    public void ConcatNullYieldsNullOff_ThenOnAgain_FindingReturns()
    {
        var findings = Scan(
            "SET CONCAT_NULL_YIELDS_NULL OFF;\n" +
            "SET CONCAT_NULL_YIELDS_NULL ON;\n" +
            "SELECT FirstName + ' ' + MiddleName FROM dbo.Person;");

        var finding = Assert.Single(findings);
        Assert.Equal("MiddleName", finding.ColumnName);
    }

    [Fact]
    public void ConcatNullYieldsNullOff_PersistsAcrossGoBatchBoundary_StillSuppressed()
    {
        var findings = Scan("SET CONCAT_NULL_YIELDS_NULL OFF;\nGO\nSELECT FirstName + ' ' + MiddleName FROM dbo.Person;");

        Assert.Empty(findings);
    }

    [Fact]
    public void ConcatNullYieldsNullOff_InUpdateSetClause_SuppressesFinding()
    {
        var findings = Scan(
            "SET CONCAT_NULL_YIELDS_NULL OFF;\nUPDATE dbo.Person SET FirstName = FirstName + ' ' + MiddleName;");

        Assert.Empty(findings);
    }

    [Fact]
    public void ConcatNullYieldsNullDefaultUntouched_StillFires()
    {
        var findings = Scan("SELECT FirstName + ' ' + MiddleName FROM dbo.Person;");

        var finding = Assert.Single(findings);
        Assert.Equal("MiddleName", finding.ColumnName);
    }

    [Fact]
    public void ThroughView_NotAnalyzed()
    {

        var ddl = """
            CREATE TABLE dbo.Person (
                Id INT NOT NULL PRIMARY KEY,
                FirstName VARCHAR(50) NOT NULL,
                MiddleName VARCHAR(50) NULL
            );
            GO
            CREATE VIEW dbo.PersonView AS SELECT FirstName, MiddleName FROM dbo.Person;
            """;
        var result = SqlScriptParser.ParseText(
            "test.sql", $"{ddl}\nGO\nSELECT FirstName + ' ' + MiddleName FROM dbo.PersonView;");
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        var catalog = CatalogBuilder.Build([result]);

        var findings = StringConcatNullScanner.Scan(result, catalog);

        Assert.Empty(findings);
    }

    [Theory]
    [InlineData("MiddleName IS NOT NULL")]
    [InlineData("MiddleName <> ''")]
    [InlineData("MiddleName LIKE 'A%'")]
    [InlineData("Age > 3 AND MiddleName = @m")]
    public void NullableOperandRejectedAsNullByTheSameQuerysWhere_NeverFires(string where)
    {
        var findings = Scan($"DECLARE @m VARCHAR(50) = 'x'; SELECT FirstName + ' ' + MiddleName FROM dbo.Person WHERE {where};");

        Assert.Empty(findings);
    }

    [Theory]
    [InlineData("MiddleName IS NULL")]
    [InlineData("Age > 3 OR MiddleName IS NOT NULL")]
    [InlineData("NOT (MiddleName IS NOT NULL)")]
    [InlineData("Age > 3")]
    [InlineData("ISNULL(MiddleName, '') <> ''")]
    public void NullableOperandNotRejectedAsNullByTheWhere_StillFires(string where)
    {
        var findings = Scan($"SELECT FirstName + ' ' + MiddleName FROM dbo.Person WHERE {where};");

        Assert.Single(findings);
    }

    [Fact]
    public void QualifiedNullableOperandRejectedByQualifiedWhere_NeverFires()
    {
        var findings = Scan("SELECT FirstName + ' ' + p.MiddleName FROM dbo.Person p WHERE p.MiddleName IS NOT NULL;");

        Assert.Empty(findings);
    }

    [Fact]
    public void DelimiterPrefixInsideForXmlPath_NeverFires()
    {
        var findings = Scan("SELECT ',' + MiddleName FROM dbo.Person FOR XML PATH('');");

        Assert.Empty(findings);
    }

    [Fact]
    public void SubqueryWithForXmlPath_DoesNotSilenceTheOuterSelect()
    {
        var findings = Scan(
            "SELECT FirstName + ' ' + MiddleName, (SELECT ',' + Label FROM dbo.Other FOR XML PATH('')) FROM dbo.Person;");

        var finding = Assert.Single(findings);
        Assert.Equal("MiddleName", finding.ColumnName);
    }

    [Fact]
    public void ForXmlRawConcatenation_StillFires()
    {
        var findings = Scan("SELECT FirstName + ' ' + MiddleName FROM dbo.Person FOR XML RAW;");

        Assert.Single(findings);
    }

    [Theory]
    [InlineData("SELECT p.Id, (SELECT p.MiddleName + 'x') FROM dbo.Person p WHERE p.MiddleName IS NOT NULL;", false)]
    [InlineData("SELECT p.Id, x.v FROM dbo.Person p CROSS APPLY (SELECT p.MiddleName + 'x' AS v) x WHERE p.MiddleName IS NOT NULL;", false)]
    [InlineData("SELECT p.Id, (SELECT p.MiddleName + 'x') FROM dbo.Person p;", true)]
    [InlineData("SELECT p.Id, (SELECT p.MiddleName + 'x') FROM dbo.Person p WHERE p.MiddleName IS NOT NULL OR p.Id > 0;", true)]
    [InlineData("SELECT p.Id, (SELECT TOP (1) p.MiddleName + 'x' FROM dbo.Person p) FROM dbo.Person p WHERE p.MiddleName IS NOT NULL;", true)]
    public void CorrelatedColumnRejectedByTheOuterQuery_NeverFires(string sql, bool fires)
    {
        Assert.Equal(fires, Scan(sql).Count > 0);
    }

    [Theory]
    [InlineData("'x' + ISNULL(MiddleName, MiddleName)", true)]
    [InlineData("'x' + COALESCE(MiddleName, MiddleName)", true)]
    [InlineData("'x' + ISNULL(MiddleName, '')", false)]
    [InlineData("'x' + COALESCE(MiddleName, LastName)", false)]
    public void AFallbackThatIsAlsoNullableStillPropagatesNull(string expression, bool fires)
    {
        var findings = Scan($"SELECT {expression} FROM dbo.Person;");

        Assert.Equal(fires, findings.Count == 1);
    }
}
