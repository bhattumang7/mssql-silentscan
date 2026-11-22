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
}
