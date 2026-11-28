using SilentScan.Core.Catalog;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;

namespace SilentScan.Tests.Predicates;

public sealed class CatchAllPredicateScannerTests
{
    private static IReadOnlyList<CatchAllPredicateFinding> Scan(string sql, int? compatibilityLevel = null)
    {
        var ddl = "CREATE TABLE dbo.Customers (Id INT NOT NULL, Code VARCHAR(20) NOT NULL, Region VARCHAR(20) NOT NULL, Segment VARCHAR(20) NOT NULL, INDEX IX_Customers_Code (Code), INDEX IX_Customers_Segment (Segment));"
            + "CREATE TABLE dbo.Orders (CustomerId INT NOT NULL, Code VARCHAR(20) NOT NULL, INDEX IX_Orders_Code (Code));";
        return ScanWithDdl(ddl, sql, compatibilityLevel);
    }

    private static IReadOnlyList<CatchAllPredicateFinding> ScanWithDdl(string ddl, string sql, int? compatibilityLevel = null, bool optionalParameterOptimizationDisabled = false)
    {
        var result = SqlScriptParser.ParseText("test.sql", $"{ddl}\nGO\n{sql}");
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));

        var catalog = CatalogBuilder.Build([result]);
        catalog.CompatibilityLevel = compatibilityLevel;
        if (optionalParameterOptimizationDisabled)
        {
            catalog.DisabledScopedConfigurations.Add("OPTIONAL_PARAMETER_OPTIMIZATION");
        }

        return CatchAllPredicateScanner.Scan(result, catalog);
    }

    [Fact]
    public void InsertSelectWithOptionRecompile_NeverFires()
    {
        var findings = Scan(
            "CREATE PROCEDURE dbo.usp_Find @p VARCHAR(20) AS BEGIN INSERT INTO dbo.Orders (CustomerId, Code) SELECT Id, Code FROM dbo.Customers WHERE (Code = @p OR @p IS NULL) OPTION (RECOMPILE); END");

        Assert.Empty(findings);
    }

    [Fact]
    public void InsertSelectWithoutAHint_AfterARecompiledInsert_StillFires()
    {
        var findings = Scan(
            "CREATE PROCEDURE dbo.usp_Find @p VARCHAR(20) AS BEGIN INSERT INTO dbo.Orders (CustomerId, Code) SELECT Id, Code FROM dbo.Customers WHERE Id = 1 OPTION (RECOMPILE); "
            + "INSERT INTO dbo.Orders (CustomerId, Code) SELECT Id, Code FROM dbo.Customers WHERE (Code = @p OR @p IS NULL); END");

        Assert.Single(findings);
    }

    [Fact]
    public void CatchAllPair_InsideUnsatisfiableAndBranch_EliminatedByNormalization()
    {
        var findings = Scan(
            "CREATE PROCEDURE dbo.usp_Find @p VARCHAR(20) AS BEGIN SELECT 1 FROM dbo.Customers WHERE (Code = @p OR @p IS NULL) AND Id = 1 AND Id = 2; END");

        Assert.Empty(findings);
    }

    [Fact]
    public void CatchAllPair_AbsorbedByEquivalentOuterConjunct_EliminatedByNormalization()
    {
        var findings = Scan(
            "CREATE PROCEDURE dbo.usp_Find @p VARCHAR(20) AS BEGIN SELECT 1 FROM dbo.Customers WHERE Code = @p AND (Code = @p OR @p IS NULL); END");

        Assert.Empty(findings);
    }

    private const string SelectProcedure =
        "CREATE PROCEDURE dbo.usp_Find @p VARCHAR(20) AS BEGIN SELECT 1 FROM dbo.Customers WHERE (Code = @p OR @p IS NULL); END";

    [Theory]
    [InlineData(170)]
    public void SelectAtACompatibilityLevelWithOptionalParameterOptimization_NeverFires(int compatibilityLevel)
    {
        Assert.Empty(Scan(SelectProcedure, compatibilityLevel));
    }

    [Fact]
    public void SelectAtCompatibilityLevel170WithTheDatabaseScopedOptimizationDisabled_StillFires()
    {
        var ddl = "CREATE TABLE dbo.Customers (Id INT NOT NULL, Code VARCHAR(20) NOT NULL, Region VARCHAR(20) NOT NULL, INDEX IX_Customers_Code (Code));";

        Assert.Single(ScanWithDdl(ddl, SelectProcedure, 170, optionalParameterOptimizationDisabled: true));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(160)]
    [InlineData(150)]
    public void SelectAtAnUnknownOrOlderCompatibilityLevel_StillFires(int? compatibilityLevel)
    {
        Assert.Single(Scan(SelectProcedure, compatibilityLevel));
    }

    [Theory]
    [InlineData("UPDATE dbo.Customers SET Region = 'x' WHERE (Code = @p OR @p IS NULL)")]
    [InlineData("DELETE dbo.Customers WHERE (Code = @p OR @p IS NULL)")]
    [InlineData("INSERT dbo.Archive (Id) SELECT Id FROM dbo.Customers WHERE (Code = @p OR @p IS NULL)")]
    public void ModificationAtCompatibilityLevel170_NeverFires(string statement)
    {
        Assert.Empty(Scan($"CREATE PROCEDURE dbo.usp_Find @p VARCHAR(20) AS BEGIN {statement}; END", 170));
    }

    [Theory]
    [InlineData("UPDATE dbo.Customers SET Region = 'x' WHERE (Code = @p OR @p IS NULL)")]
    [InlineData("DELETE dbo.Customers WHERE (Code = @p OR @p IS NULL)")]
    public void ModificationAtCompatibilityLevel160_StillFires(string statement)
    {
        Assert.Single(Scan($"CREATE PROCEDURE dbo.usp_Find @p VARCHAR(20) AS BEGIN {statement}; END", 160));
    }

    [Fact]
    public void CanonicalOrder_ColumnEqualsParameterOrParameterIsNull_Fires()
    {
        var findings = Scan(
            "CREATE PROCEDURE dbo.usp_Find @p VARCHAR(20) AS BEGIN SELECT 1 FROM dbo.Customers WHERE (Code = @p OR @p IS NULL); END");

        var finding = Assert.Single(findings);
        Assert.Equal("dbo.Customers", finding.TableQualifiedName);
        Assert.Equal("Code", finding.ColumnName);
        Assert.Equal("@p", finding.ParameterName);
        Assert.True(finding.Indexed);
    }

    [Fact]
    public void SwappedOrder_ParameterIsNullOrColumnEqualsParameter_Fires()
    {
        var findings = Scan(
            "CREATE PROCEDURE dbo.usp_Find @p VARCHAR(20) AS BEGIN SELECT 1 FROM dbo.Customers WHERE (@p IS NULL OR Code = @p); END");

        var finding = Assert.Single(findings);
        Assert.Equal("Code", finding.ColumnName);
    }

    [Fact]
    public void ChainedIndependentClauses_FiresOncePerMatchedPair()
    {
        var findings = Scan(
            """
            CREATE PROCEDURE dbo.usp_Find @p VARCHAR(20), @q VARCHAR(20) AS
            BEGIN
                SELECT 1 FROM dbo.Customers WHERE (Code = @p OR @p IS NULL) AND (Segment = @q OR @q IS NULL);
            END
            """);

        Assert.Equal(2, findings.Count);
        Assert.Contains(findings, f => f.ColumnName == "Code" && f.ParameterName == "@p");
        Assert.Contains(findings, f => f.ColumnName == "Segment" && f.ParameterName == "@q");
    }

    [Fact]
    public void CteSharesNameWithIndexedBaseTable_AttributesThroughToTheRealUnderlyingColumn()
    {

        var findings = Scan(
            """
            CREATE PROCEDURE dbo.usp_Find @p VARCHAR(20) AS
            BEGIN
                WITH Customers AS (SELECT Code AS Region FROM dbo.Customers)
                SELECT 1 FROM Customers WHERE (Region = @p OR @p IS NULL);
            END
            """);

        var finding = Assert.Single(findings);
        Assert.Equal("dbo.Customers", finding.TableQualifiedName);
        Assert.Equal("Code", finding.ColumnName);
        Assert.True(finding.Indexed);
    }

    [Fact]
    public void CteAliasesAnUnindexedColumnAsAnIndexedName_AttributionFollowsTheRealColumn_NoFinding()
    {
        var findings = Scan(
            """
            CREATE PROCEDURE dbo.usp_Find @p VARCHAR(20) AS
            BEGIN
                WITH Customers AS (SELECT Region AS Code FROM dbo.Customers)
                SELECT 1 FROM Customers WHERE (Code = @p OR @p IS NULL);
            END
            """);

        Assert.Empty(findings);
    }

    [Fact]
    public void UnindexedColumn_NoSeekExistsToLose_NeverFires()
    {
        var findings = Scan(
            "CREATE PROCEDURE dbo.usp_Find @p VARCHAR(20) AS BEGIN SELECT 1 FROM dbo.Customers WHERE (Region = @p OR @p IS NULL); END");

        Assert.Empty(findings);
    }

    [Fact]
    public void IndexedAndUnindexedCatchAllsInOneStatement_OnlyTheIndexedOneFires()
    {
        var findings = Scan(
            "CREATE PROCEDURE dbo.usp_Find @p VARCHAR(20), @q VARCHAR(20) AS BEGIN SELECT 1 FROM dbo.Customers WHERE (Code = @p OR @p IS NULL) AND (Region = @q OR @q IS NULL); END");

        var finding = Assert.Single(findings);
        Assert.Equal("Code", finding.ColumnName);
        Assert.True(finding.Indexed);
    }

    [Fact]
    public void DeclaredLocalVariable_NotAFormalParameter_NeverFires()
    {

        var findings = Scan(
            "CREATE PROCEDURE dbo.usp_Find AS BEGIN DECLARE @p VARCHAR(20) = 'ABC'; SELECT 1 FROM dbo.Customers WHERE (Code = @p OR @p IS NULL); END");

        Assert.Empty(findings);
    }

    [Fact]
    public void DifferentVariablesInEqualityAndIsNull_NeverFires()
    {
        var findings = Scan(
            "CREATE PROCEDURE dbo.usp_Find @p VARCHAR(20), @q VARCHAR(20) AS BEGIN SELECT 1 FROM dbo.Customers WHERE (Code = @p OR @q IS NULL); END");

        Assert.Empty(findings);
    }

    [Fact]
    public void WrappedColumn_NeverFires()
    {

        var findings = Scan(
            "CREATE PROCEDURE dbo.usp_Find @p VARCHAR(20) AS BEGIN SELECT 1 FROM dbo.Customers WHERE (UPPER(Code) = @p OR @p IS NULL); END");

        Assert.Empty(findings);
    }

    [Fact]
    public void UnrelatedOr_NeverFires()
    {
        var findings = Scan(
            "CREATE PROCEDURE dbo.usp_Find @p VARCHAR(20), @q VARCHAR(20) AS BEGIN SELECT 1 FROM dbo.Customers WHERE (Code = @p OR Region = @q); END");

        Assert.Empty(findings);
    }

    [Fact]
    public void PlainEquality_NoOr_NeverFires()
    {
        var findings = Scan(
            "CREATE PROCEDURE dbo.usp_Find @p VARCHAR(20) AS BEGIN SELECT 1 FROM dbo.Customers WHERE Code = @p; END");

        Assert.Empty(findings);
    }

    [Fact]
    public void NegatedCatchAllShape_NeverFires()
    {

        var findings = Scan(
            "CREATE PROCEDURE dbo.usp_Find @p VARCHAR(20) AS BEGIN SELECT 1 FROM dbo.Customers WHERE NOT (Code = @p OR @p IS NULL); END");

        Assert.Empty(findings);
    }

    [Fact]
    public void StatementOptionRecompile_SuppressesTheFinding()
    {
        var findings = Scan(
            "CREATE PROCEDURE dbo.usp_Find @p VARCHAR(20) AS BEGIN SELECT 1 FROM dbo.Customers WHERE (Code = @p OR @p IS NULL) OPTION (RECOMPILE); END");

        Assert.Empty(findings);
    }

    [Fact]
    public void ProcedureWithRecompile_SuppressesEveryFindingInTheBody()
    {
        var findings = Scan(
            "CREATE PROCEDURE dbo.usp_Find @p VARCHAR(20) WITH RECOMPILE AS BEGIN SELECT 1 FROM dbo.Customers WHERE (Code = @p OR @p IS NULL); END");

        Assert.Empty(findings);
    }

    [Fact]
    public void CanonicalOrder_InJoinOnClause_Fires()
    {
        var findings = Scan(
            "CREATE PROCEDURE dbo.usp_Find @p VARCHAR(20) AS BEGIN "
            + "SELECT 1 FROM dbo.Customers c JOIN dbo.Orders o ON (c.Code = @p OR @p IS NULL) WHERE o.CustomerId = c.Id; END");

        var finding = Assert.Single(findings);
        Assert.Equal("Code", finding.ColumnName);
    }

    [Fact]
    public void CanonicalOrder_InHavingClause_Fires()
    {
        var findings = Scan(
            "CREATE PROCEDURE dbo.usp_Find @p VARCHAR(20) AS BEGIN "
            + "SELECT Code FROM dbo.Customers GROUP BY Code HAVING (Code = @p OR @p IS NULL); END");

        var finding = Assert.Single(findings);
        Assert.Equal("Code", finding.ColumnName);
    }

    [Fact]
    public void UpdateStatement_SameShapeInWhereClause_Fires()
    {
        var findings = Scan(
            "CREATE PROCEDURE dbo.usp_Find @p VARCHAR(20) AS BEGIN UPDATE dbo.Customers SET Region = 'X' WHERE (Code = @p OR @p IS NULL); END");

        var finding = Assert.Single(findings);
        Assert.Equal("Code", finding.ColumnName);
    }

    [Fact]
    public void AdHocBatchParameter_NoEnclosingProcedure_NeverFires()
    {

        var findings = Scan("DECLARE @p VARCHAR(20) = 'ABC'; SELECT 1 FROM dbo.Customers WHERE (Code = @p OR @p IS NULL);");

        Assert.Empty(findings);
    }

    [Fact]
    public void AlterProcedure_SameShape_Fires()
    {
        var findings = Scan(
            "ALTER PROCEDURE dbo.usp_Find @p VARCHAR(20) AS BEGIN SELECT 1 FROM dbo.Customers WHERE (Code = @p OR @p IS NULL); END");

        Assert.Single(findings);
    }

    [Fact]
    public void CreateOrAlterProcedure_SameShape_Fires()
    {
        var findings = Scan(
            "CREATE OR ALTER PROCEDURE dbo.usp_Find @p VARCHAR(20) AS BEGIN SELECT 1 FROM dbo.Customers WHERE (Code = @p OR @p IS NULL); END");

        Assert.Single(findings);
    }

    [Fact]
    public void CreateFunction_SameShape_Fires()
    {
        var findings = Scan(
            "CREATE FUNCTION dbo.usf_Find (@p VARCHAR(20)) RETURNS INT AS BEGIN DECLARE @r INT = (SELECT COUNT(*) FROM dbo.Customers WHERE (Code = @p OR @p IS NULL)); RETURN @r; END");

        Assert.Single(findings);
    }

    [Fact]
    public void CreateTrigger_ParametersAlwaysEmpty_NeverFires()
    {
        var findings = Scan(
            "CREATE TRIGGER dbo.trg_Customers ON dbo.Customers AFTER INSERT AS BEGIN SELECT 1 FROM dbo.Customers WHERE Code = 'x'; END");

        Assert.Empty(findings);
    }

    [Fact]
    public void AlterFunction_SameShape_Fires()
    {
        var findings = Scan(
            "ALTER FUNCTION dbo.usf_Find (@p VARCHAR(20)) RETURNS INT AS BEGIN DECLARE @r INT = (SELECT COUNT(*) FROM dbo.Customers WHERE (Code = @p OR @p IS NULL)); RETURN @r; END");

        Assert.Single(findings);
    }

    [Fact]
    public void CreateOrAlterFunction_SameShape_Fires()
    {
        var findings = Scan(
            "CREATE OR ALTER FUNCTION dbo.usf_Find (@p VARCHAR(20)) RETURNS INT AS BEGIN DECLARE @r INT = (SELECT COUNT(*) FROM dbo.Customers WHERE (Code = @p OR @p IS NULL)); RETURN @r; END");

        Assert.Single(findings);
    }

    [Fact]
    public void AlterTrigger_ParametersAlwaysEmpty_NeverFires()
    {
        var findings = Scan(
            "ALTER TRIGGER dbo.trg_Customers ON dbo.Customers AFTER INSERT AS BEGIN SELECT 1 FROM dbo.Customers WHERE Code = 'x'; END");

        Assert.Empty(findings);
    }

    [Fact]
    public void CreateOrAlterTrigger_ParametersAlwaysEmpty_NeverFires()
    {
        var findings = Scan(
            "CREATE OR ALTER TRIGGER dbo.trg_Customers ON dbo.Customers AFTER INSERT AS BEGIN SELECT 1 FROM dbo.Customers WHERE Code = 'x'; END");

        Assert.Empty(findings);
    }

    [Fact]
    public void DeleteStatement_SameShapeInWhereClause_Fires()
    {
        var findings = Scan(
            "CREATE PROCEDURE dbo.usp_Find @p VARCHAR(20) AS BEGIN DELETE FROM dbo.Customers WHERE (Code = @p OR @p IS NULL); END");

        var finding = Assert.Single(findings);
        Assert.Equal("Code", finding.ColumnName);
    }

    [Fact]
    public void DeleteStatement_WithRecompileHint_NeverFires()
    {
        var findings = Scan(
            "CREATE PROCEDURE dbo.usp_Find @p VARCHAR(20) AS BEGIN DELETE FROM dbo.Customers WHERE (Code = @p OR @p IS NULL) OPTION (RECOMPILE); END");

        Assert.Empty(findings);
    }

    [Fact]
    public void MergeStatement_NoStandaloneWhereClause_NeverThrows()
    {
        var findings = Scan(
            "CREATE PROCEDURE dbo.usp_Find @p VARCHAR(20) AS BEGIN "
            + "MERGE dbo.Customers AS t USING (SELECT @p AS Code) AS s ON t.Code = s.Code "
            + "WHEN MATCHED THEN UPDATE SET t.Region = 'X' "
            + "WHEN NOT MATCHED THEN INSERT (Id, Code, Region) VALUES (1, s.Code, 'X'); END");

        Assert.Empty(findings);
    }

    [Fact]
    public void OrLeavesWrappedInParentheses_StillDetected()
    {
        var findings = Scan(
            "CREATE PROCEDURE dbo.usp_Find @p VARCHAR(20) AS BEGIN SELECT 1 FROM dbo.Customers WHERE (Code = @p) OR (@p IS NULL); END");

        var finding = Assert.Single(findings);
        Assert.Equal("Code", finding.ColumnName);
    }

    [Fact]
    public void ColumnFromCte_NotABaseColumnAtDepthZero_NeverFires()
    {
        var findings = Scan(
            "CREATE PROCEDURE dbo.usp_Find @p VARCHAR(20) AS BEGIN "
            + "WITH C AS (SELECT Code + '' AS Code FROM dbo.Customers) "
            + "SELECT 1 FROM C WHERE (C.Code = @p OR @p IS NULL); END");

        Assert.Empty(findings);
    }

    [Fact]
    public void CatchAllPair_InsideExistsSubqueryGuardedByUnsatisfiableOuterAliasPredicate_ShouldBeEliminatedByNormalization()
    {
        var findings = Scan(
            """
            CREATE PROCEDURE dbo.usp_Find @p VARCHAR(20) AS
            BEGIN
                SELECT 1 FROM dbo.Customers c
                WHERE EXISTS (
                    SELECT 1 FROM dbo.Orders o
                    WHERE o.CustomerId = c.Id AND c.Id = 1 AND c.Id = 2 AND (o.Code = @p OR @p IS NULL)
                );
            END
            """);

        Assert.Empty(findings);
    }

    [Fact]
    public void CatchAllPair_InsideExistsSubqueryGuardedByOuterAliasCaseSensitiveColumnCompareSameValueDifferentCase_ShouldBeEliminatedByNormalization()
    {
        var ddl =
            "CREATE TABLE dbo.Customers (Id INT NOT NULL, Code VARCHAR(20) COLLATE Latin1_General_CS_AS NOT NULL, INDEX IX_Customers_Code (Code));"
            + "CREATE TABLE dbo.Orders (CustomerId INT NOT NULL, Code VARCHAR(20) NOT NULL, INDEX IX_Orders_Code (Code));";

        var findings = ScanWithDdl(
            ddl,
            """
            CREATE PROCEDURE dbo.usp_Find @p VARCHAR(20) AS
            BEGIN
                SELECT 1 FROM dbo.Customers c
                WHERE EXISTS (
                    SELECT 1 FROM dbo.Orders o
                    WHERE o.CustomerId = c.Id AND c.Code = 'ABC' AND c.Code = 'abc' AND (o.Code = @p OR @p IS NULL)
                );
            END
            """);

        Assert.Empty(findings);
    }

    [Theory]
    [InlineData("Code = @c AND (Region = @p OR @p IS NULL)")]
    [InlineData("(Region = @p OR @p IS NULL) AND Code > @c")]
    [InlineData("Code = 'A' AND (Region = @p OR @p IS NULL)")]
    public void CatchAllConjunct_ResidualBehindSargablePredicateOnIndexedLeadingColumn_DoesNotFire(string where)
    {
        var findings = Scan(
            $"CREATE PROCEDURE dbo.usp_Find @p VARCHAR(20), @c VARCHAR(20) AS BEGIN SELECT 1 FROM dbo.Customers WHERE {where}; END");

        Assert.Empty(findings);
    }

    [Theory]
    [InlineData("Segment = @c AND (Code = @p OR @p IS NULL)")]
    [InlineData("(Code = @c OR Region = @c) AND (Region = @p OR @p IS NULL)")]
    [InlineData("Code = Region AND (Region = @p OR @p IS NULL)")]
    [InlineData("UPPER(Code) = @c AND (Region = @p OR @p IS NULL)")]
    public void CatchAllConjunct_WithoutASargableIndexedSibling_StillFires(string where)
    {
        var findings = ScanWithDdl(
            "CREATE TABLE dbo.Customers (Id INT NOT NULL, Code VARCHAR(20) NOT NULL, Region VARCHAR(20) NOT NULL, Segment VARCHAR(20) NOT NULL, INDEX IX_Customers_Code (Code), INDEX IX_Customers_Region (Region));",
            $"CREATE PROCEDURE dbo.usp_Find @p VARCHAR(20), @c VARCHAR(20) AS BEGIN SELECT 1 FROM dbo.Customers WHERE {where}; END",
            compatibilityLevel: null);

        Assert.Single(findings);
    }

    [Theory]
    [InlineData("SELECT Id FROM dbo.Customers WHERE Code = @p OR @p IS NULL;", false)]
    [InlineData("UPDATE dbo.Customers SET Region = 'x' WHERE Code = @p OR @p IS NULL;", false)]
    [InlineData("DECLARE @x INT; SELECT @x = Id FROM dbo.Customers WHERE Code = @p OR @p IS NULL;", true)]
    [InlineData("DECLARE @x INT; SET @x = (SELECT COUNT(*) FROM dbo.Customers WHERE Code = @p OR @p IS NULL);", true)]
    [InlineData("IF EXISTS (SELECT 1 FROM dbo.Customers WHERE Code = @p OR @p IS NULL) SELECT 1;", true)]
    [InlineData("DECLARE c CURSOR LOCAL FOR SELECT Id FROM dbo.Customers WHERE Code = @p OR @p IS NULL;", true)]
    [InlineData("DECLARE @t TABLE (Id INT); INSERT INTO @t SELECT Id FROM dbo.Customers WHERE Code = @p OR @p IS NULL;", true)]
    public void AtCompat170_StatementsWithoutAPlanPerBranch_StillFire(string statement, bool fires)
    {
        var findings = Scan($"CREATE PROCEDURE dbo.usp_Find @p VARCHAR(20) AS BEGIN {statement} END", compatibilityLevel: 170);

        Assert.Equal(fires, findings.Count > 0);
    }
}
