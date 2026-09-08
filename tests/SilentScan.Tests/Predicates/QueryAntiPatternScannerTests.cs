using SilentScan.Core.Catalog;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;

namespace SilentScan.Tests.Predicates;

public sealed class QueryAntiPatternScannerTests
{
    private const string Ddl =
        "CREATE TABLE dbo.Big (Id INT NOT NULL PRIMARY KEY, Col VARCHAR(20) NOT NULL);"
        + "CREATE TABLE dbo.A (Id INT NOT NULL PRIMARY KEY);"
        + "CREATE TABLE dbo.B (Id INT NOT NULL PRIMARY KEY, AId INT NOT NULL);"
        + "CREATE UNIQUE INDEX UX_B_AId ON dbo.B(AId);"
        + "CREATE TABLE dbo.C (Id INT NOT NULL PRIMARY KEY, AId INT NOT NULL);";

    private static IReadOnlyList<QueryAntiPatternFinding> Scan(string sql, int? compatibilityLevel = null)
    {
        var result = SqlScriptParser.ParseText("test.sql", $"{Ddl}\nGO\n{sql}");
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));

        var catalog = CatalogBuilder.Build([result]);
        catalog.CompatibilityLevel = compatibilityLevel;
        return QueryAntiPatternScanner.Scan(result, catalog);
    }

    [Fact]
    public void TableValuedParameterReadAsTableSource_AtCompat170_FiresTableVariablePspSkip()
    {
        var findings = Scan(
            "CREATE TYPE dbo.IdList AS TABLE (Id INT NOT NULL PRIMARY KEY);\nGO\n"
            + "CREATE PROCEDURE dbo.P @ids dbo.IdList READONLY AS SELECT Id FROM @ids;",
            compatibilityLevel: 170);

        var finding = Assert.Single(findings, f => f.Kind == QueryAntiPatternFindingKind.TableVariablePspSkip);
        Assert.Equal("@ids", finding.DetailText);
        Assert.Equal(FindingConfidence.High, finding.Confidence);
    }

    [Fact]
    public void TableValuedParameterReadAsTableSource_BelowCompat170_NeverFiresTableVariablePspSkip()
    {
        var findings = Scan(
            "CREATE TYPE dbo.IdList AS TABLE (Id INT NOT NULL PRIMARY KEY);\nGO\n"
            + "CREATE PROCEDURE dbo.P @ids dbo.IdList READONLY AS SELECT Id FROM @ids;",
            compatibilityLevel: 160);

        Assert.DoesNotContain(findings, f => f.Kind == QueryAntiPatternFindingKind.TableVariablePspSkip);
    }

    [Fact]
    public void TableValuedParameter_DeclaredButNeverReadAsTableSource_NeverFiresTableVariablePspSkip()
    {
        var findings = Scan(
            "CREATE TYPE dbo.IdList AS TABLE (Id INT NOT NULL PRIMARY KEY);\nGO\n"
            + "CREATE PROCEDURE dbo.P @ids dbo.IdList READONLY AS SELECT 1;",
            compatibilityLevel: 170);

        Assert.DoesNotContain(findings, f => f.Kind == QueryAntiPatternFindingKind.TableVariablePspSkip);
    }

    [Fact]
    public void TableValuedParameter_ReadInOneStatementOnly_OnlyFiresForTheStatementThatReadsIt()
    {
        var findings = Scan(
            "CREATE TYPE dbo.IdList AS TABLE (Id INT NOT NULL PRIMARY KEY);\nGO\n"
            + "CREATE PROCEDURE dbo.P @CustomerId INT, @ids dbo.IdList READONLY AS "
            + "DECLARE @c INT = (SELECT COUNT(*) FROM @ids); "
            + "SELECT Id FROM dbo.Big WHERE Id = @CustomerId;",
            compatibilityLevel: 170);

        var finding = Assert.Single(findings, f => f.Kind == QueryAntiPatternFindingKind.TableVariablePspSkip);
        Assert.Equal("@ids", finding.DetailText);
    }

    [Fact]
    public void TableVariableAsJoinSource_BelowCompat150_Fires()
    {
        var findings = Scan(
            "DECLARE @t TABLE (Id INT); INSERT INTO @t SELECT Id FROM dbo.Big; "
            + "SELECT b.Id FROM dbo.Big b JOIN @t t ON b.Id = t.Id;",
            compatibilityLevel: 130);

        var finding = Assert.Single(findings, f => f.Kind == QueryAntiPatternFindingKind.TableVariableLowCompatEstimate);
        Assert.Equal(FindingConfidence.High, finding.Confidence);
    }

    [Fact]
    public void TableVariableAsJoinSource_AtCompat150_NeverFiresLowCompatKind()
    {
        var findings = Scan(
            "DECLARE @t TABLE (Id INT); INSERT INTO @t SELECT Id FROM dbo.Big; "
            + "SELECT b.Id FROM dbo.Big b JOIN @t t ON b.Id = t.Id;",
            compatibilityLevel: 150);

        Assert.DoesNotContain(findings, f => f.Kind == QueryAntiPatternFindingKind.TableVariableLowCompatEstimate);
    }

    [Fact]
    public void TableVariableAsJoinSource_UnknownCompat_NeverFiresLowCompatKind()
    {
        var findings = Scan(
            "DECLARE @t TABLE (Id INT); INSERT INTO @t SELECT Id FROM dbo.Big; "
            + "SELECT b.Id FROM dbo.Big b JOIN @t t ON b.Id = t.Id;");

        Assert.DoesNotContain(findings, f => f.Kind == QueryAntiPatternFindingKind.TableVariableLowCompatEstimate);
    }

    [Fact]
    public void TableVariableReadAndWrittenInSameLoop_UnknownCompat_Fires()
    {
        var findings = Scan(
            "DECLARE @t TABLE (Id INT); DECLARE @i INT = 0; DECLARE @c INT; "
            + "WHILE @i < 5 BEGIN "
            + "INSERT INTO @t SELECT Id FROM dbo.Big WHERE Id = @i; "
            + "SELECT @c = COUNT(Id) FROM @t; "
            + "SET @i = @i + 1; END;");

        var finding = Assert.Single(findings, f => f.Kind == QueryAntiPatternFindingKind.TableVariableStaleEstimateInLoop);
        Assert.Equal(FindingConfidence.Medium, finding.Confidence);
    }

    [Fact]
    public void TableVariableReadOnlyNoWriteInLoop_NeverFires()
    {
        var findings = Scan(
            "DECLARE @t TABLE (Id INT); INSERT INTO @t SELECT Id FROM dbo.Big; "
            + "DECLARE @i INT = 0; DECLARE @c INT; "
            + "WHILE @i < 5 BEGIN SELECT @c = COUNT(Id) FROM @t; SET @i = @i + 1; END;");

        Assert.DoesNotContain(findings, f => f.Kind == QueryAntiPatternFindingKind.TableVariableStaleEstimateInLoop);
    }

    [Fact]
    public void TableVariableReadAndWrittenInSameLoop_BelowCompat150_ReportsOnlyLowCompatKind()
    {

        var findings = Scan(
            "DECLARE @t TABLE (Id INT); DECLARE @i INT = 0; DECLARE @c INT; "
            + "WHILE @i < 5 BEGIN "
            + "INSERT INTO @t SELECT Id FROM dbo.Big WHERE Id = @i; "
            + "SELECT @c = COUNT(Id) FROM @t; "
            + "SET @i = @i + 1; END;",
            compatibilityLevel: 130);

        Assert.DoesNotContain(findings, f => f.Kind == QueryAntiPatternFindingKind.TableVariableStaleEstimateInLoop);
    }

    [Fact]
    public void WhileLoopUpdateKeyedToLoopVariable_Fires()
    {
        var findings = Scan(
            "DECLARE @i INT = 0; "
            + "WHILE @i < 100 BEGIN "
            + "UPDATE dbo.Big SET Col = 'x' WHERE Id = @i; "
            + "SET @i = @i + 1; END;");

        var finding = Assert.Single(findings, f => f.Kind == QueryAntiPatternFindingKind.RbarSingleRowLoopDml);
        Assert.Contains("Id", finding.DetailText);
    }

    [Fact]
    public void WhileLoopDeleteKeyedToCursorFetchedVariable_Fires()
    {
        var findings = Scan(
            "DECLARE @id INT; DECLARE cur CURSOR LOCAL FOR SELECT Id FROM dbo.Big; "
            + "OPEN cur; FETCH NEXT FROM cur INTO @id; "
            + "WHILE @@FETCH_STATUS = 0 BEGIN "
            + "DELETE FROM dbo.Big WHERE Id = @id; "
            + "FETCH NEXT FROM cur INTO @id; END; "
            + "CLOSE cur; DEALLOCATE cur;");

        Assert.Single(findings, f => f.Kind == QueryAntiPatternFindingKind.RbarSingleRowLoopDml);
    }

    [Fact]
    public void WhileLoopUpdateWithCompositePredicate_NeverFires()
    {
        var findings = Scan(
            "DECLARE @i INT = 0; "
            + "WHILE @i < 100 BEGIN "
            + "UPDATE dbo.Big SET Col = 'x' WHERE Id = @i AND Col = 'y'; "
            + "SET @i = @i + 1; END;");

        Assert.DoesNotContain(findings, f => f.Kind == QueryAntiPatternFindingKind.RbarSingleRowLoopDml);
    }

    [Fact]
    public void UpdateOutsideAnyLoop_NeverFires()
    {
        var findings = Scan("DECLARE @i INT = 1; UPDATE dbo.Big SET Col = 'x' WHERE Id = @i;");

        Assert.DoesNotContain(findings, f => f.Kind == QueryAntiPatternFindingKind.RbarSingleRowLoopDml);
    }

    [Fact]
    public void CursorDeclaredWithoutLocal_Fires()
    {
        var findings = Scan("DECLARE cur CURSOR FOR SELECT Id FROM dbo.Big; OPEN cur; CLOSE cur; DEALLOCATE cur;");

        var finding = Assert.Single(findings, f => f.Kind == QueryAntiPatternFindingKind.GlobalCursorDeclaration);
        Assert.Equal(FindingConfidence.Low, finding.Confidence);
    }

    [Fact]
    public void CursorDeclaredExplicitGlobal_Fires()
    {
        var findings = Scan("DECLARE cur CURSOR GLOBAL FOR SELECT Id FROM dbo.Big; OPEN cur; CLOSE cur; DEALLOCATE cur;");

        Assert.Single(findings, f => f.Kind == QueryAntiPatternFindingKind.GlobalCursorDeclaration);
    }

    [Fact]
    public void CursorDeclaredLocal_NeverFires()
    {
        var findings = Scan("DECLARE cur CURSOR LOCAL FOR SELECT Id FROM dbo.Big; OPEN cur; CLOSE cur; DEALLOCATE cur;");

        Assert.DoesNotContain(findings, f => f.Kind == QueryAntiPatternFindingKind.GlobalCursorDeclaration);
    }

    [Fact]
    public void CountStarAssignedThenComparedToZeroInNextStatement_Fires()
    {
        var findings = Scan(
            "DECLARE @cnt INT; SELECT @cnt = COUNT(*) FROM dbo.Big WHERE Col = 'x'; IF @cnt > 0 SELECT 1;");

        var finding = Assert.Single(findings, f => f.Kind == QueryAntiPatternFindingKind.CountStarVariableExistenceCheck);
        Assert.Equal(FindingConfidence.High, finding.Confidence);
    }

    [Theory]
    [InlineData("IF @cnt >= 1 SELECT 1;")]
    [InlineData("IF @cnt = 0 SELECT 1;")]
    [InlineData("IF @cnt <> 0 SELECT 1;")]
    [InlineData("IF 0 = @cnt SELECT 1;")]
    [InlineData("IF 0 < @cnt SELECT 1;")]
    public void CountStarAssignedThenComparedToZero_VariousForms_Fire(string ifStatement)
    {
        var findings = Scan($"DECLARE @cnt INT; SELECT @cnt = COUNT(*) FROM dbo.Big; {ifStatement}");

        Assert.Single(findings, f => f.Kind == QueryAntiPatternFindingKind.CountStarVariableExistenceCheck);
    }

    [Fact]
    public void InlineCountStarScalarSubquery_NeverFires()
    {

        var findings = Scan("IF (SELECT COUNT(*) FROM dbo.Big WHERE Col = 'x') > 0 SELECT 1;");

        Assert.DoesNotContain(findings, f => f.Kind == QueryAntiPatternFindingKind.CountStarVariableExistenceCheck);
    }

    [Fact]
    public void CountStarAssignedButNotComparedInVeryNextStatement_NeverFires()
    {
        var findings = Scan(
            "DECLARE @cnt INT; SELECT @cnt = COUNT(*) FROM dbo.Big; PRINT 'x'; IF @cnt > 0 SELECT 1;");

        Assert.DoesNotContain(findings, f => f.Kind == QueryAntiPatternFindingKind.CountStarVariableExistenceCheck);
    }

    [Fact]
    public void CountStarAssignedThenUsedForItsMagnitude_NeverFires()
    {
        var findings = Scan(
            "DECLARE @cnt INT; SELECT @cnt = COUNT(*) FROM dbo.Big; PRINT @cnt;");

        Assert.DoesNotContain(findings, f => f.Kind == QueryAntiPatternFindingKind.CountStarVariableExistenceCheck);
    }

    [Fact]
    public void HavingConditionOnGroupByKeyOnly_Fires()
    {
        var findings = Scan("SELECT Col, COUNT(*) FROM dbo.Big GROUP BY Col HAVING Col = 'x';");

        var finding = Assert.Single(findings, f => f.Kind == QueryAntiPatternFindingKind.NonAggregateHavingPredicate);
        Assert.Equal(FindingConfidence.High, finding.Confidence);
    }

    [Fact]
    public void HavingConditionOnAggregateResult_NeverFires()
    {
        var findings = Scan("SELECT Col, COUNT(*) FROM dbo.Big GROUP BY Col HAVING COUNT(*) > 1;");

        Assert.DoesNotContain(findings, f => f.Kind == QueryAntiPatternFindingKind.NonAggregateHavingPredicate);
    }

    [Fact]
    public void HavingConditionMixingKeyAndAggregate_FiresForTheKeyOnlyBranch()
    {

        var findings = Scan("SELECT Col, COUNT(*) FROM dbo.Big GROUP BY Col HAVING Col = 'x' AND COUNT(*) > 1;");

        var finding = Assert.Single(findings, f => f.Kind == QueryAntiPatternFindingKind.NonAggregateHavingPredicate);
        Assert.Contains("GROUP BY key", finding.DetailText);
    }

    [Fact]
    public void HavingConditionOredWithAggregate_NeverFires()
    {

        var findings = Scan("SELECT Col, COUNT(*) FROM dbo.Big GROUP BY Col HAVING Col = 'x' OR COUNT(*) > 1;");

        Assert.DoesNotContain(findings, f => f.Kind == QueryAntiPatternFindingKind.NonAggregateHavingPredicate);
    }

    [Fact]
    public void HavingConditionInsideUnsatisfiableConjunct_NeverFires()
    {
        var findings = Scan("SELECT Id, COUNT(*) FROM dbo.Big GROUP BY Id HAVING Id = 1 AND Id = 2;");

        Assert.DoesNotContain(findings, f => f.Kind == QueryAntiPatternFindingKind.NonAggregateHavingPredicate);
    }

    [Fact]
    public void UnionOfTwoDistinctLiteralEqualityBranches_Fires()
    {
        var findings = Scan(
            "SELECT * FROM dbo.Big WHERE Col = 'a' UNION SELECT * FROM dbo.Big WHERE Col = 'b';");

        var finding = Assert.Single(findings, f => f.Kind == QueryAntiPatternFindingKind.UnionOfProvablyDisjointBranches);
        Assert.Equal(FindingConfidence.Medium, finding.Confidence);
    }

    [Fact]
    public void UnionOfThreeDistinctLiteralEqualityBranches_Fires()
    {
        var findings = Scan(
            "SELECT * FROM dbo.Big WHERE Col = 'a' "
            + "UNION SELECT * FROM dbo.Big WHERE Col = 'b' "
            + "UNION SELECT * FROM dbo.Big WHERE Col = 'c';");

        Assert.Single(findings, f => f.Kind == QueryAntiPatternFindingKind.UnionOfProvablyDisjointBranches);
    }

    [Fact]
    public void UnionAll_NeverFires()
    {
        var findings = Scan(
            "SELECT * FROM dbo.Big WHERE Col = 'a' UNION ALL SELECT * FROM dbo.Big WHERE Col = 'b';");

        Assert.DoesNotContain(findings, f => f.Kind == QueryAntiPatternFindingKind.UnionOfProvablyDisjointBranches);
    }

    [Fact]
    public void UnionWithOverlappingLiteral_NeverFires()
    {
        var findings = Scan(
            "SELECT * FROM dbo.Big WHERE Col = 'a' UNION SELECT * FROM dbo.Big WHERE Col = 'a';");

        Assert.DoesNotContain(findings, f => f.Kind == QueryAntiPatternFindingKind.UnionOfProvablyDisjointBranches);
    }

    [Fact]
    public void UnionWithJoinBranch_NeverFires()
    {
        var findings = Scan(
            "SELECT a.Id FROM dbo.A a JOIN dbo.B b ON a.Id = b.AId WHERE a.Id = 1 "
            + "UNION SELECT Id FROM dbo.A WHERE Id = 2;");

        Assert.DoesNotContain(findings, f => f.Kind == QueryAntiPatternFindingKind.UnionOfProvablyDisjointBranches);
    }

    [Fact]
    public void SelectDistinctJoinOnNonUniqueColumn_Fires()
    {
        var findings = Scan("SELECT DISTINCT a.Id FROM dbo.A a JOIN dbo.C c ON a.Id = c.AId;");

        var finding = Assert.Single(findings, f => f.Kind == QueryAntiPatternFindingKind.DistinctMaskingJoinFanout);
        Assert.Equal(FindingConfidence.Medium, finding.Confidence);
    }

    [Fact]
    public void SelectDistinctJoinOnUniqueIndexedColumn_NeverFires()
    {
        var findings = Scan("SELECT DISTINCT a.Id FROM dbo.A a JOIN dbo.B b ON a.Id = b.AId;");

        Assert.DoesNotContain(findings, f => f.Kind == QueryAntiPatternFindingKind.DistinctMaskingJoinFanout);
    }

    [Fact]
    public void PlainSelectJoinOnNonUniqueColumn_NoDistinct_NeverFires()
    {
        var findings = Scan("SELECT a.Id FROM dbo.A a JOIN dbo.C c ON a.Id = c.AId;");

        Assert.DoesNotContain(findings, f => f.Kind == QueryAntiPatternFindingKind.DistinctMaskingJoinFanout);
    }

    [Fact]
    public void SelectDistinctJoinOnNonUniqueColumn_WhereClauseUnsatisfiable_NeverFires()
    {
        var findings = Scan("SELECT DISTINCT a.Id FROM dbo.A a JOIN dbo.C c ON a.Id = c.AId WHERE a.Id = 1 AND a.Id = 2;");

        Assert.DoesNotContain(findings, f => f.Kind == QueryAntiPatternFindingKind.DistinctMaskingJoinFanout);
    }

    [Fact]
    public void SelectDistinctJoinOnNonUniqueColumn_InsideCorrelatedExists_ControlStillFires()
    {
        var findings = Scan(
            "CREATE TABLE dbo.OuterTagged (Id INT NOT NULL, Tag VARCHAR(20) COLLATE Latin1_General_CS_AS NOT NULL);\nGO\n"
            + "SELECT 1 FROM dbo.OuterTagged outerA WHERE EXISTS ("
            + "SELECT DISTINCT a.Id FROM dbo.A a JOIN dbo.C c ON a.Id = c.AId WHERE outerA.Tag = 'ABC');");

        Assert.Single(findings, f => f.Kind == QueryAntiPatternFindingKind.DistinctMaskingJoinFanout);
    }

    [Fact]
    public void SelectDistinctJoinOnNonUniqueColumn_UnsatisfiableViaOuterAliasCaseSensitiveColumn_InsideCorrelatedExists_NeverFires()
    {
        var findings = Scan(
            "CREATE TABLE dbo.OuterTagged (Id INT NOT NULL, Tag VARCHAR(20) COLLATE Latin1_General_CS_AS NOT NULL);\nGO\n"
            + "SELECT 1 FROM dbo.OuterTagged outerA WHERE EXISTS ("
            + "SELECT DISTINCT a.Id FROM dbo.A a JOIN dbo.C c ON a.Id = c.AId WHERE outerA.Tag = 'ABC' AND outerA.Tag = 'abc');");

        Assert.DoesNotContain(findings, f => f.Kind == QueryAntiPatternFindingKind.DistinctMaskingJoinFanout);
    }

    [Fact]
    public void UnqualifiedTableReferenceResolvingToRealTable_Fires()
    {
        var findings = Scan("SELECT Id FROM Big WHERE Id = 1;");

        var finding = Assert.Single(findings, f => f.Kind == QueryAntiPatternFindingKind.UnqualifiedTableReference);
        Assert.Equal(FindingConfidence.Medium, finding.Confidence);
    }

    [Fact]
    public void QualifiedTableReference_NeverFiresUnqualified()
    {
        var findings = Scan("SELECT Id FROM dbo.Big WHERE Id = 1;");

        Assert.DoesNotContain(findings, f => f.Kind == QueryAntiPatternFindingKind.UnqualifiedTableReference);
    }

    [Fact]
    public void UnqualifiedCteReference_NeverFiresUnqualified()
    {
        var findings = Scan(
            "WITH Big AS (SELECT Id FROM dbo.A) SELECT Id FROM Big;");

        Assert.DoesNotContain(findings, f => f.Kind == QueryAntiPatternFindingKind.UnqualifiedTableReference);
    }

    [Fact]
    public void UnqualifiedTempTableReference_NeverFires()
    {
        var findings = Scan("CREATE TABLE #t (Id INT); SELECT Id FROM #t;");

        Assert.DoesNotContain(findings, f => f.Kind == QueryAntiPatternFindingKind.UnqualifiedTableReference);
    }

    [Fact]
    public void UnqualifiedReferenceToNonexistentTable_NeverFires()
    {
        var findings = Scan("SELECT Id FROM NoSuchTable WHERE Id = 1;");

        Assert.DoesNotContain(findings, f => f.Kind == QueryAntiPatternFindingKind.UnqualifiedTableReference);
    }

    [Fact]
    public void MergeTargetWithNoHoldlockHint_Fires()
    {
        var findings = Scan(
            "MERGE dbo.A AS t USING dbo.B AS s ON t.Id = s.AId "
            + "WHEN MATCHED THEN UPDATE SET t.Id = t.Id "
            + "WHEN NOT MATCHED THEN INSERT (Id) VALUES (s.AId);");

        var finding = Assert.Single(findings, f => f.Kind == QueryAntiPatternFindingKind.MergeMissingHoldlock);
        Assert.Equal(FindingConfidence.Medium, finding.Confidence);
    }

    [Fact]
    public void MergeTargetWithHoldlockHint_NeverFires()
    {
        var findings = Scan(
            "MERGE dbo.A WITH (HOLDLOCK) AS t USING dbo.B AS s ON t.Id = s.AId "
            + "WHEN MATCHED THEN UPDATE SET t.Id = t.Id "
            + "WHEN NOT MATCHED THEN INSERT (Id) VALUES (s.AId);");

        Assert.DoesNotContain(findings, f => f.Kind == QueryAntiPatternFindingKind.MergeMissingHoldlock);
    }

    [Fact]
    public void MergeUnconditionalWhenMatchedDelete_Fires()
    {
        var findings = Scan(
            "MERGE dbo.A WITH (HOLDLOCK) AS t USING dbo.B AS s ON t.Id = s.AId "
            + "WHEN MATCHED THEN DELETE;");

        var finding = Assert.Single(findings, f => f.Kind == QueryAntiPatternFindingKind.MergeUnconditionalDelete);
        Assert.Equal(FindingConfidence.Medium, finding.Confidence);
    }

    [Fact]
    public void MergeUnconditionalWhenNotMatchedBySourceDelete_Fires()
    {
        var findings = Scan(
            "MERGE dbo.A WITH (HOLDLOCK) AS t USING dbo.B AS s ON t.Id = s.AId "
            + "WHEN NOT MATCHED BY SOURCE THEN DELETE;");

        Assert.Single(findings, f => f.Kind == QueryAntiPatternFindingKind.MergeUnconditionalDelete);
    }

    [Fact]
    public void MergeConditionallyQualifiedDelete_NeverFires()
    {
        var findings = Scan(
            "MERGE dbo.A WITH (HOLDLOCK) AS t USING dbo.B AS s ON t.Id = s.AId "
            + "WHEN MATCHED AND s.AId > 0 THEN DELETE;");

        Assert.DoesNotContain(findings, f => f.Kind == QueryAntiPatternFindingKind.MergeUnconditionalDelete);
    }

    [Fact]
    public void RecursiveCteWithNoMaxRecursionOption_Fires()
    {
        var findings = Scan(
            "WITH r AS (SELECT Id FROM dbo.A WHERE Id = 1 UNION ALL SELECT a.Id FROM dbo.A a JOIN r ON a.Id = r.Id + 1) "
            + "SELECT Id FROM r;");

        var finding = Assert.Single(findings, f => f.Kind == QueryAntiPatternFindingKind.RecursiveCteMissingMaxRecursion);
        Assert.Equal(FindingConfidence.High, finding.Confidence);
    }

    [Fact]
    public void RecursiveCteWithMaxRecursionOption_NeverFires()
    {
        var findings = Scan(
            "WITH r AS (SELECT Id FROM dbo.A WHERE Id = 1 UNION ALL SELECT a.Id FROM dbo.A a JOIN r ON a.Id = r.Id + 1) "
            + "SELECT Id FROM r OPTION (MAXRECURSION 500);");

        Assert.DoesNotContain(findings, f => f.Kind == QueryAntiPatternFindingKind.RecursiveCteMissingMaxRecursion);
    }

    [Fact]
    public void NonRecursiveCte_NeverFiresMaxRecursion()
    {
        var findings = Scan(
            "WITH r AS (SELECT Id FROM dbo.A) SELECT Id FROM r;");

        Assert.DoesNotContain(findings, f => f.Kind == QueryAntiPatternFindingKind.RecursiveCteMissingMaxRecursion);
    }

    [Fact]
    public void RecursiveCteSelfReference_UnderCaseSensitiveCollation_MismatchedCaseDoesNotSelfReference()
    {
        var result = SqlScriptParser.ParseText("test.sql", $"{Ddl}\nGO\n"
            + "WITH r AS (SELECT Id FROM dbo.A WHERE Id = 1 UNION ALL SELECT a.Id FROM dbo.A a JOIN R ON a.Id = R.Id + 1) "
            + "SELECT Id FROM r;");
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));

        var catalog = CatalogBuilder.Build([result], manifestDeclaredCollation: "Latin1_General_CS_AS");
        var findings = QueryAntiPatternScanner.Scan(result, catalog);

        Assert.DoesNotContain(findings, f => f.Kind == QueryAntiPatternFindingKind.RecursiveCteMissingMaxRecursion);
    }

    [Fact]
    public void RecursiveCteSelfReference_UnderCaseInsensitiveCollation_MismatchedCaseStillSelfReferences()
    {
        var result = SqlScriptParser.ParseText("test.sql", $"{Ddl}\nGO\n"
            + "WITH r AS (SELECT Id FROM dbo.A WHERE Id = 1 UNION ALL SELECT a.Id FROM dbo.A a JOIN R ON a.Id = R.Id + 1) "
            + "SELECT Id FROM r;");
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));

        var catalog = CatalogBuilder.Build([result], manifestDeclaredCollation: "SQL_Latin1_General_CP1_CI_AS");
        var findings = QueryAntiPatternScanner.Scan(result, catalog);

        Assert.Contains(findings, f => f.Kind == QueryAntiPatternFindingKind.RecursiveCteMissingMaxRecursion);
    }

    [Fact]
    public void UpdateWithNoWhereNoTop_Fires()
    {
        var findings = Scan("UPDATE dbo.Big SET Col = 'x';");

        var finding = Assert.Single(findings, f => f.Kind == QueryAntiPatternFindingKind.UnboundedTableWrite);
        Assert.Equal(FindingConfidence.Medium, finding.Confidence);
    }

    [Fact]
    public void DeleteWithNoWhereNoTop_Fires()
    {
        var findings = Scan("DELETE FROM dbo.Big;");

        Assert.Single(findings, f => f.Kind == QueryAntiPatternFindingKind.UnboundedTableWrite);
    }

    [Fact]
    public void UpdateWithWhere_NeverFires()
    {
        var findings = Scan("UPDATE dbo.Big SET Col = 'x' WHERE Id = 1;");

        Assert.DoesNotContain(findings, f => f.Kind == QueryAntiPatternFindingKind.UnboundedTableWrite);
    }

    [Fact]
    public void DeleteWithTop_NeverFires()
    {
        var findings = Scan("DELETE TOP (10) FROM dbo.Big;");

        Assert.DoesNotContain(findings, f => f.Kind == QueryAntiPatternFindingKind.UnboundedTableWrite);
    }

    [Fact]
    public void FourPartLinkedServerReference_Fires()
    {
        var findings = Scan("SELECT Id FROM RemoteServer.RemoteDb.dbo.RemoteTable;");

        var finding = Assert.Single(findings, f => f.Kind == QueryAntiPatternFindingKind.LinkedServerOrCrossDatabaseReference);
        Assert.Equal(FindingConfidence.High, finding.Confidence);
    }

    [Fact]
    public void ThreePartReference_FileMode_NeverFires()
    {

        var findings = Scan("SELECT Id FROM OtherDb.dbo.T;");

        Assert.DoesNotContain(findings, f => f.Kind == QueryAntiPatternFindingKind.LinkedServerOrCrossDatabaseReference);
    }

    [Fact]
    public void ThreePartReference_LiveMode_DifferentDatabase_Fires()
    {
        var result = SqlScriptParser.ParseText("test.sql", $"{Ddl}\nGO\nSELECT Id FROM OtherDb.dbo.T;");
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        var catalog = CatalogBuilder.Build([result]);
        catalog.CurrentDatabaseName = "ThisDb";

        var findings = QueryAntiPatternScanner.Scan(result, catalog);

        var finding = Assert.Single(findings, f => f.Kind == QueryAntiPatternFindingKind.LinkedServerOrCrossDatabaseReference);
        Assert.Equal(FindingConfidence.Medium, finding.Confidence);
    }

    [Fact]
    public void ThreePartReference_LiveMode_SystemDatabase_NeverFires()
    {

        var result = SqlScriptParser.ParseText("test.sql", $"{Ddl}\nGO\nSELECT object_id FROM tempdb.sys.objects;");
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        var catalog = CatalogBuilder.Build([result]);
        catalog.CurrentDatabaseName = "ThisDb";

        var findings = QueryAntiPatternScanner.Scan(result, catalog);

        Assert.DoesNotContain(findings, f => f.Kind == QueryAntiPatternFindingKind.LinkedServerOrCrossDatabaseReference);
    }

    [Fact]
    public void ThreePartReference_LiveMode_SameDatabase_NeverFires()
    {
        var result = SqlScriptParser.ParseText("test.sql", $"{Ddl}\nGO\nSELECT Id FROM ThisDb.dbo.Big;");
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        var catalog = CatalogBuilder.Build([result]);
        catalog.CurrentDatabaseName = "ThisDb";

        var findings = QueryAntiPatternScanner.Scan(result, catalog);

        Assert.DoesNotContain(findings, f => f.Kind == QueryAntiPatternFindingKind.LinkedServerOrCrossDatabaseReference);
    }

    private static DatabaseCatalog CatalogWithCouponTable(bool ignoreDupKey)
    {
        var ddl = "CREATE TABLE dbo.Coupon (Code VARCHAR(20) NOT NULL, Pct INT NOT NULL);";
        var result = SqlScriptParser.ParseText("test.sql", ddl);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        var catalog = CatalogBuilder.Build([result]);

        var existing = catalog.Find("dbo.Coupon")!;
        var index = new CatalogIndex(
            "UX_Coupon_Code", CatalogIndexKind.UniqueConstraint, IsUnique: true, KeyColumns: ["Code"],
            IncludedColumns: [], IgnoreDupKey: ignoreDupKey);
        catalog.AddOrReplace(existing with { Indexes = [index] });
        return catalog;
    }

    private static IReadOnlyList<QueryAntiPatternFinding> ScanCoupon(string sql, bool ignoreDupKey)
    {
        var result = SqlScriptParser.ParseText("test.sql", sql);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        return QueryAntiPatternScanner.Scan(result, CatalogWithCouponTable(ignoreDupKey));
    }

    [Fact]
    public void MultiRowInsert_IntoIgnoreDupKeyUniqueIndex_Fires()
    {
        var findings = ScanCoupon(
            "INSERT INTO dbo.Coupon (Code, Pct) VALUES ('SAVE10', 10), ('SAVE20', 20), ('SAVE10', 15);",
            ignoreDupKey: true);

        var finding = Assert.Single(findings, f => f.Kind == QueryAntiPatternFindingKind.MultiRowInsertIgnoreDupKeyDrop);
        Assert.Equal(FindingConfidence.High, finding.Confidence);
        Assert.Contains("UX_Coupon_Code", finding.DetailText);
    }

    [Fact]
    public void SingleRowInsert_IntoIgnoreDupKeyUniqueIndex_NeverFires()
    {

        var findings = ScanCoupon(
            "INSERT INTO dbo.Coupon (Code, Pct) VALUES ('SAVE10', 10);",
            ignoreDupKey: true);

        Assert.DoesNotContain(findings, f => f.Kind == QueryAntiPatternFindingKind.MultiRowInsertIgnoreDupKeyDrop);
    }

    [Fact]
    public void MultiRowInsert_IntoOrdinaryUniqueIndex_NeverFires()
    {

        var findings = ScanCoupon(
            "INSERT INTO dbo.Coupon (Code, Pct) VALUES ('SAVE10', 10), ('SAVE20', 20), ('SAVE10', 15);",
            ignoreDupKey: false);

        Assert.DoesNotContain(findings, f => f.Kind == QueryAntiPatternFindingKind.MultiRowInsertIgnoreDupKeyDrop);
    }

    [Fact]
    public void MultiRowInsertSelect_IntoIgnoreDupKeyUniqueIndex_NeverFires()
    {

        var findings = ScanCoupon(
            "CREATE TABLE dbo.CouponSource (Code VARCHAR(20) NOT NULL, Pct INT NOT NULL); "
            + "INSERT INTO dbo.Coupon (Code, Pct) SELECT Code, Pct FROM dbo.CouponSource;",
            ignoreDupKey: true);

        Assert.DoesNotContain(findings, f => f.Kind == QueryAntiPatternFindingKind.MultiRowInsertIgnoreDupKeyDrop);
    }

    [Fact]
    public void MultiRowInsert_IntoIgnoreDupKeyUniqueIndex_FollowedByRowCountGuard_NeverFires()
    {
        var findings = ScanCoupon(
            """
            INSERT INTO dbo.Coupon (Code, Pct) VALUES ('SAVE10', 10), ('SAVE20', 20), ('SAVE10', 15);
            IF @@ROWCOUNT <> 3
            BEGIN
                THROW 51000, 'One or more coupon codes were duplicates and were not inserted.', 1;
            END
            """,
            ignoreDupKey: true);

        Assert.DoesNotContain(findings, f => f.Kind == QueryAntiPatternFindingKind.MultiRowInsertIgnoreDupKeyDrop);
    }

    private static IReadOnlyList<QueryAntiPatternFinding> ScanSwitch(string ddl, string switchSql)
    {
        var result = SqlScriptParser.ParseText("test.sql", $"{ddl}\nGO\n{switchSql}");
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        var catalog = CatalogBuilder.Build([result]);
        return QueryAntiPatternScanner.Scan(result, catalog);
    }

    private static DatabaseCatalog CatalogWithSwitchTables(
        IReadOnlyList<CatalogIndex> sourceIndexes, IReadOnlyList<CatalogIndex> targetIndexes)
    {
        var ddl = "CREATE TABLE dbo.SwSrc (Id INT NOT NULL, Code VARCHAR(20) NOT NULL, Pct INT NOT NULL); "
            + "CREATE TABLE dbo.SwTgt (Id INT NOT NULL, Code VARCHAR(20) NOT NULL, Pct INT NOT NULL);";
        var result = SqlScriptParser.ParseText("test.sql", ddl);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        var catalog = CatalogBuilder.Build([result]);

        catalog.AddOrReplace(catalog.Find("dbo.SwSrc")! with { Indexes = sourceIndexes });
        catalog.AddOrReplace(catalog.Find("dbo.SwTgt")! with { Indexes = targetIndexes });
        return catalog;
    }

    private static IReadOnlyList<QueryAntiPatternFinding> ScanSwitchIndexes(
        IReadOnlyList<CatalogIndex> sourceIndexes, IReadOnlyList<CatalogIndex> targetIndexes)
    {
        var result = SqlScriptParser.ParseText("test.sql", "ALTER TABLE dbo.SwSrc SWITCH TO dbo.SwTgt;");
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        return QueryAntiPatternScanner.Scan(result, CatalogWithSwitchTables(sourceIndexes, targetIndexes));
    }

    private static DatabaseCatalog CatalogWithSwitchConstraints(
        IReadOnlyList<CatalogCheckConstraint> checkConstraints, IReadOnlyList<ForeignKeyRelationship> foreignKeys)
    {
        var ddl = "CREATE TABLE dbo.SwRef (Id INT NOT NULL); "
            + "CREATE TABLE dbo.SwSrc (Id INT NOT NULL, RegionId INT NOT NULL); "
            + "CREATE TABLE dbo.SwTgt (Id INT NOT NULL, RegionId INT NOT NULL);";
        var result = SqlScriptParser.ParseText("test.sql", ddl);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        var catalog = CatalogBuilder.Build([result]);

        foreach (var check in checkConstraints)
        {
            catalog.AddCheckConstraint(check);
        }

        foreach (var fk in foreignKeys)
        {
            catalog.AddForeignKey(fk);
        }

        return catalog;
    }

    private static IReadOnlyList<QueryAntiPatternFinding> ScanSwitchConstraints(
        IReadOnlyList<CatalogCheckConstraint> checkConstraints, IReadOnlyList<ForeignKeyRelationship> foreignKeys)
    {
        var result = SqlScriptParser.ParseText("test.sql", "ALTER TABLE dbo.SwSrc SWITCH TO dbo.SwTgt;");
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        return QueryAntiPatternScanner.Scan(result, CatalogWithSwitchConstraints(checkConstraints, foreignKeys));
    }

    private static IReadOnlyList<QueryAntiPatternFinding> ScanSwitchTargetOnlyIndexes(
        IReadOnlyList<CatalogIndex> sourceIndexes, IReadOnlyList<CatalogIndex> targetIndexes)
    {
        var result = SqlScriptParser.ParseText("test.sql", "ALTER TABLE dbo.SwSrc SWITCH TO dbo.SwTgt;");
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        return QueryAntiPatternScanner.Scan(result, CatalogWithSwitchTables(sourceIndexes, targetIndexes));
    }

    private static DatabaseCatalog CatalogWithSwitchFullTextIndexes(bool sourceHasFullTextIndex, bool targetHasFullTextIndex)
    {
        var ddl = "CREATE TABLE dbo.SwSrc (Id INT NOT NULL); CREATE TABLE dbo.SwTgt (Id INT NOT NULL);";
        var result = SqlScriptParser.ParseText("test.sql", ddl);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        var catalog = CatalogBuilder.Build([result]);

        catalog.AddOrReplace(catalog.Find("dbo.SwSrc")! with { HasFullTextIndex = sourceHasFullTextIndex });
        catalog.AddOrReplace(catalog.Find("dbo.SwTgt")! with { HasFullTextIndex = targetHasFullTextIndex });
        return catalog;
    }

    private static IReadOnlyList<QueryAntiPatternFinding> ScanSwitchFullTextIndexes(bool sourceHasFullTextIndex, bool targetHasFullTextIndex)
    {
        var result = SqlScriptParser.ParseText("test.sql", "ALTER TABLE dbo.SwSrc SWITCH TO dbo.SwTgt;");
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        return QueryAntiPatternScanner.Scan(result, CatalogWithSwitchFullTextIndexes(sourceHasFullTextIndex, targetHasFullTextIndex));
    }

    private static DatabaseCatalog CatalogWithSwitchFilegroups(
        string? sourceFilegroup, bool sourceReadOnly, string? targetFilegroup, bool targetReadOnly)
    {
        var ddl = "CREATE TABLE dbo.SwSrc (Id INT NOT NULL); CREATE TABLE dbo.SwTgt (Id INT NOT NULL);";
        var result = SqlScriptParser.ParseText("test.sql", ddl);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        var catalog = CatalogBuilder.Build([result]);

        catalog.AddOrReplace(catalog.Find("dbo.SwSrc")! with { FilegroupName = sourceFilegroup, FilegroupIsReadOnly = sourceReadOnly });
        catalog.AddOrReplace(catalog.Find("dbo.SwTgt")! with { FilegroupName = targetFilegroup, FilegroupIsReadOnly = targetReadOnly });
        return catalog;
    }

    private static IReadOnlyList<QueryAntiPatternFinding> ScanSwitchFilegroups(
        string? sourceFilegroup, bool sourceReadOnly, string? targetFilegroup, bool targetReadOnly)
    {
        var result = SqlScriptParser.ParseText("test.sql", "ALTER TABLE dbo.SwSrc SWITCH TO dbo.SwTgt;");
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        return QueryAntiPatternScanner.Scan(result, CatalogWithSwitchFilegroups(sourceFilegroup, sourceReadOnly, targetFilegroup, targetReadOnly));
    }

    private static DatabaseCatalog CatalogWithSwitchTemporal(bool sourceIsTemporal, bool targetIsTemporal)
    {
        var ddl = "CREATE TABLE dbo.SwSrc (Id INT NOT NULL); CREATE TABLE dbo.SwTgt (Id INT NOT NULL);";
        var result = SqlScriptParser.ParseText("test.sql", ddl);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        var catalog = CatalogBuilder.Build([result]);

        if (sourceIsTemporal)
        {
            catalog.AddTemporalTablePair(new TemporalTablePair("dbo.SwSrc", "dbo.SwSrcHistory"));
        }

        if (targetIsTemporal)
        {
            catalog.AddTemporalTablePair(new TemporalTablePair("dbo.SwTgt", "dbo.SwTgtHistory"));
        }

        return catalog;
    }

    private static IReadOnlyList<QueryAntiPatternFinding> ScanSwitchTemporal(bool sourceIsTemporal, bool targetIsTemporal)
    {
        var result = SqlScriptParser.ParseText("test.sql", "ALTER TABLE dbo.SwSrc SWITCH TO dbo.SwTgt;");
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        return QueryAntiPatternScanner.Scan(result, CatalogWithSwitchTemporal(sourceIsTemporal, targetIsTemporal));
    }

    private static DatabaseCatalog CatalogWithSwitchRuleConstraint(bool sourceHasRule, bool targetHasRule)
    {
        var ddl = "CREATE TABLE dbo.SwSrc (Id INT NOT NULL); CREATE TABLE dbo.SwTgt (Id INT NOT NULL);";
        var result = SqlScriptParser.ParseText("test.sql", ddl);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        var catalog = CatalogBuilder.Build([result]);

        catalog.AddOrReplace(catalog.Find("dbo.SwSrc")! with { HasRuleConstraint = sourceHasRule });
        catalog.AddOrReplace(catalog.Find("dbo.SwTgt")! with { HasRuleConstraint = targetHasRule });
        return catalog;
    }

    private static IReadOnlyList<QueryAntiPatternFinding> ScanSwitchRuleConstraint(bool sourceHasRule, bool targetHasRule)
    {
        var result = SqlScriptParser.ParseText("test.sql", "ALTER TABLE dbo.SwSrc SWITCH TO dbo.SwTgt;");
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        return QueryAntiPatternScanner.Scan(result, CatalogWithSwitchRuleConstraint(sourceHasRule, targetHasRule));
    }

    private static DatabaseCatalog CatalogWithSwitchCdc(bool sourceDisallowed, bool targetDisallowed)
    {
        var ddl = "CREATE TABLE dbo.SwSrc (Id INT NOT NULL); CREATE TABLE dbo.SwTgt (Id INT NOT NULL);";
        var result = SqlScriptParser.ParseText("test.sql", ddl);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        var catalog = CatalogBuilder.Build([result]);

        catalog.AddOrReplace(catalog.Find("dbo.SwSrc")! with { CdcPartitionSwitchDisallowed = sourceDisallowed });
        catalog.AddOrReplace(catalog.Find("dbo.SwTgt")! with { CdcPartitionSwitchDisallowed = targetDisallowed });
        return catalog;
    }

    private static IReadOnlyList<QueryAntiPatternFinding> ScanSwitchCdc(string switchSql, bool sourceDisallowed, bool targetDisallowed)
    {
        var result = SqlScriptParser.ParseText("test.sql", switchSql);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        return QueryAntiPatternScanner.Scan(result, CatalogWithSwitchCdc(sourceDisallowed, targetDisallowed));
    }

    private static DatabaseCatalog CatalogWithSwitchPartitionFilegroups(
        string? sourceScheme, string? targetScheme, IEnumerable<(string Scheme, int PartitionNumber, string Filegroup)> mappings)
    {
        var ddl = "CREATE TABLE dbo.SwSrc (Id INT NOT NULL); CREATE TABLE dbo.SwTgt (Id INT NOT NULL);";
        var result = SqlScriptParser.ParseText("test.sql", ddl);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        var catalog = CatalogBuilder.Build([result]);

        catalog.AddOrReplace(catalog.Find("dbo.SwSrc")! with { PartitionSchemeName = sourceScheme });
        catalog.AddOrReplace(catalog.Find("dbo.SwTgt")! with { PartitionSchemeName = targetScheme });

        foreach (var (scheme, partitionNumber, filegroup) in mappings)
        {
            catalog.AddPartitionFilegroup(scheme, partitionNumber, filegroup);
        }

        return catalog;
    }

    private static IReadOnlyList<QueryAntiPatternFinding> ScanSwitchPartitionFilegroups(
        string switchSql, string? sourceScheme, string? targetScheme, IEnumerable<(string Scheme, int PartitionNumber, string Filegroup)> mappings)
    {
        var result = SqlScriptParser.ParseText("test.sql", switchSql);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        return QueryAntiPatternScanner.Scan(result, CatalogWithSwitchPartitionFilegroups(sourceScheme, targetScheme, mappings));
    }

    [Fact]
    public void AlterTableSwitch_NeitherTableResolves_NoFindingsAndDoesNotThrow()
    {
        var findings = ScanSwitch(
            "CREATE TABLE dbo.SwOther (Id INT NOT NULL);",
            "ALTER TABLE dbo.NoSuchSrc SWITCH PARTITION 1 TO dbo.NoSuchTgt PARTITION 1;");

        Assert.Empty(findings);
    }

    [Fact]
    public void AlterProcedure_TableValuedParameterReadAsTableSource_AtCompat170_FiresTableVariablePspSkip()
    {
        var findings = Scan(
            "CREATE TYPE dbo.IdList AS TABLE (Id INT NOT NULL PRIMARY KEY);\nGO\n"
            + "ALTER PROCEDURE dbo.P @ids dbo.IdList READONLY AS SELECT Id FROM @ids;",
            compatibilityLevel: 170);

        Assert.Single(findings, f => f.Kind == QueryAntiPatternFindingKind.TableVariablePspSkip);
    }

    [Fact]
    public void CreateOrAlterProcedure_TableValuedParameterReadAsTableSource_AtCompat170_FiresTableVariablePspSkip()
    {
        var findings = Scan(
            "CREATE TYPE dbo.IdList AS TABLE (Id INT NOT NULL PRIMARY KEY);\nGO\n"
            + "CREATE OR ALTER PROCEDURE dbo.P @ids dbo.IdList READONLY AS SELECT Id FROM @ids;",
            compatibilityLevel: 170);

        Assert.Single(findings, f => f.Kind == QueryAntiPatternFindingKind.TableVariablePspSkip);
    }

    [Fact]
    public void AlterFunction_TableValuedParameterReadAsTableSource_AtCompat170_FiresTableVariablePspSkip()
    {
        var findings = Scan(
            "CREATE TYPE dbo.IdList AS TABLE (Id INT NOT NULL PRIMARY KEY);\nGO\n"
            + "ALTER FUNCTION dbo.F (@ids dbo.IdList READONLY) RETURNS INT AS BEGIN DECLARE @c INT = (SELECT COUNT(*) FROM @ids); RETURN @c; END;",
            compatibilityLevel: 170);

        Assert.Single(findings, f => f.Kind == QueryAntiPatternFindingKind.TableVariablePspSkip);
    }

    [Fact]
    public void CreateOrAlterFunction_TableValuedParameterReadAsTableSource_AtCompat170_FiresTableVariablePspSkip()
    {
        var findings = Scan(
            "CREATE TYPE dbo.IdList AS TABLE (Id INT NOT NULL PRIMARY KEY);\nGO\n"
            + "CREATE OR ALTER FUNCTION dbo.F (@ids dbo.IdList READONLY) RETURNS INT AS BEGIN DECLARE @c INT = (SELECT COUNT(*) FROM @ids); RETURN @c; END;",
            compatibilityLevel: 170);

        Assert.Single(findings, f => f.Kind == QueryAntiPatternFindingKind.TableVariablePspSkip);
    }

    [Fact]
    public void ScalarParameter_AtCompat170_NeverFiresTableVariablePspSkip()
    {
        var findings = Scan(
            "CREATE PROCEDURE dbo.P @id INT AS SELECT @id;",
            compatibilityLevel: 170);

        Assert.DoesNotContain(findings, f => f.Kind == QueryAntiPatternFindingKind.TableVariablePspSkip);
    }

    [Fact]
    public void TableValuedParameterUsedInFromClause_BelowCompat150_NeverFiresLowCompatKind()
    {
        var findings = Scan(
            "CREATE TYPE dbo.IdList AS TABLE (Id INT NOT NULL PRIMARY KEY);\nGO\n"
            + "CREATE PROCEDURE dbo.P @ids dbo.IdList READONLY AS SELECT Id FROM @ids;",
            compatibilityLevel: 130);

        Assert.DoesNotContain(findings, f => f.Kind == QueryAntiPatternFindingKind.TableVariableLowCompatEstimate);
    }

    [Fact]
    public void SetVariableAssignedFromCursorDefinition_WithoutLocal_Fires()
    {
        var findings = Scan(
            "DECLARE @c CURSOR; SET @c = CURSOR FOR SELECT Id FROM dbo.Big; OPEN @c; CLOSE @c; DEALLOCATE @c;");

        var finding = Assert.Single(findings, f => f.Kind == QueryAntiPatternFindingKind.GlobalCursorDeclaration);
        Assert.Equal("@c (no LOCAL/GLOBAL keyword, defaults to GLOBAL)", finding.DetailText);
    }

    [Fact]
    public void MultiRowInsert_IntoUnresolvableTable_NeverFires()
    {
        var findings = Scan("INSERT INTO dbo.NoSuchTable (Id) VALUES (1), (2);");

        Assert.DoesNotContain(findings, f => f.Kind == QueryAntiPatternFindingKind.MultiRowInsertIgnoreDupKeyDrop);
    }

    [Fact]
    public void ParenthesizedJoin_UnqualifiedTableReferenceInsideParentheses_Fires()
    {
        var findings = Scan("SELECT a.Id FROM (Big a JOIN dbo.A b ON a.Id = b.Id);");

        Assert.Contains(findings, f => f.Kind == QueryAntiPatternFindingKind.UnqualifiedTableReference);
    }

    [Fact]
    public void MergeTargetIsTableVariable_NeverFiresMissingHoldlock()
    {
        var findings = Scan(
            "DECLARE @t TABLE (Id INT NOT NULL, AId INT NOT NULL); "
            + "MERGE @t AS t USING dbo.B AS s ON t.Id = s.AId "
            + "WHEN MATCHED THEN UPDATE SET t.Id = t.Id "
            + "WHEN NOT MATCHED THEN INSERT (Id, AId) VALUES (s.AId, s.AId);");

        Assert.DoesNotContain(findings, f => f.Kind == QueryAntiPatternFindingKind.MergeMissingHoldlock);
    }

    [Fact]
    public void CreateFunction_TableValuedParameterReadAsTableSource_AtCompat170_FiresTableVariablePspSkip()
    {
        var findings = Scan(
            "CREATE TYPE dbo.IdList AS TABLE (Id INT NOT NULL PRIMARY KEY);\nGO\n"
            + "CREATE FUNCTION dbo.F (@ids dbo.IdList READONLY) RETURNS INT AS BEGIN DECLARE @c INT = (SELECT COUNT(*) FROM @ids); RETURN @c; END;",
            compatibilityLevel: 170);

        Assert.Single(findings, f => f.Kind == QueryAntiPatternFindingKind.TableVariablePspSkip);
    }

    [Fact]
    public void TableVariableReadThroughParenthesizedJoinInLoop_Fires()
    {
        var findings = Scan(
            "DECLARE @t TABLE (Id INT); DECLARE @i INT = 0; DECLARE @c INT; "
            + "WHILE @i < 5 BEGIN "
            + "INSERT INTO @t SELECT Id FROM dbo.Big WHERE Id = @i; "
            + "SELECT @c = COUNT(*) FROM (@t t JOIN dbo.A a ON t.Id = a.Id); "
            + "SET @i = @i + 1; END;");

        Assert.Single(findings, f => f.Kind == QueryAntiPatternFindingKind.TableVariableStaleEstimateInLoop);
    }

    [Fact]
    public void NestedWhileLoop_InnerLoopReadNotTraversedForOuterLoopAnalysis()
    {
        var findings = Scan(
            "DECLARE @t TABLE (Id INT); DECLARE @i INT = 0; DECLARE @c INT; "
            + "WHILE @i < 5 BEGIN "
            + "INSERT INTO @t SELECT Id FROM dbo.Big WHERE Id = @i; "
            + "WHILE 1 = 0 BEGIN SELECT @c = COUNT(*) FROM @t; BREAK; END; "
            + "SET @i = @i + 1; END;");

        Assert.DoesNotContain(findings, f => f.Kind == QueryAntiPatternFindingKind.TableVariableStaleEstimateInLoop);
    }

    [Fact]
    public void WhileLoopMergeIntoTableVariable_ReadOfSameVariable_Fires()
    {
        var findings = Scan(
            "DECLARE @t TABLE (Id INT NOT NULL, AId INT NOT NULL); DECLARE @i INT = 0; DECLARE @c INT; "
            + "WHILE @i < 5 BEGIN "
            + "MERGE @t AS tgt USING dbo.B AS s ON tgt.Id = s.AId "
            + "WHEN MATCHED THEN UPDATE SET tgt.Id = tgt.Id "
            + "WHEN NOT MATCHED THEN INSERT (Id, AId) VALUES (s.AId, s.AId); "
            + "SELECT @c = COUNT(*) FROM @t; "
            + "SET @i = @i + 1; END;");

        Assert.Single(findings, f => f.Kind == QueryAntiPatternFindingKind.TableVariableStaleEstimateInLoop);
    }

    [Fact]
    public void WhileLoopUpdateOutputIntoTableVariable_ReadOfSameVariable_Fires()
    {
        var findings = Scan(
            "DECLARE @t TABLE (Id INT); DECLARE @i INT = 0; DECLARE @c INT; "
            + "WHILE @i < 5 BEGIN "
            + "UPDATE dbo.Big SET Col = 'x' OUTPUT inserted.Id INTO @t WHERE Id = @i; "
            + "SELECT @c = COUNT(*) FROM @t; "
            + "SET @i = @i + 1; END;");

        Assert.Single(findings, f => f.Kind == QueryAntiPatternFindingKind.TableVariableStaleEstimateInLoop);
    }

    [Fact]
    public void WhileLoopUpdateWithVariableOnLeftOfComparison_Fires()
    {
        var findings = Scan(
            "DECLARE @i INT = 0; "
            + "WHILE @i < 100 BEGIN "
            + "UPDATE dbo.Big SET Col = 'x' WHERE @i = Id; "
            + "SET @i = @i + 1; END;");

        Assert.Single(findings, f => f.Kind == QueryAntiPatternFindingKind.RbarSingleRowLoopDml);
    }

    [Fact]
    public void NestedWhileLoop_InnerLoopDmlNotTraversedForOuterLoopAnalysis()
    {
        var findings = Scan(
            "DECLARE @i INT = 0; "
            + "WHILE @i < 100 BEGIN "
            + "WHILE 1 = 0 BEGIN UPDATE dbo.Big SET Col = 'x' WHERE Id = @i; BREAK; END; "
            + "SET @i = @i + 1; END;");

        Assert.DoesNotContain(findings, f => f.Kind == QueryAntiPatternFindingKind.RbarSingleRowLoopDml);
    }

    [Fact]
    public void WhileLoopUpdateComparingTwoColumns_NeverFires()
    {
        var findings = Scan(
            "DECLARE @i INT = 0; "
            + "WHILE @i < 100 BEGIN "
            + "UPDATE dbo.C SET AId = 1 WHERE Id = AId; "
            + "SET @i = @i + 1; END;");

        Assert.DoesNotContain(findings, f => f.Kind == QueryAntiPatternFindingKind.RbarSingleRowLoopDml);
    }

    [Fact]
    public void CountStarAssignedThenComparedInFollowingWhileLoop_Fires()
    {
        var findings = Scan(
            "DECLARE @cnt INT; SELECT @cnt = COUNT(*) FROM dbo.Big; WHILE @cnt > 0 BEGIN BREAK; END;");

        Assert.Single(findings, f => f.Kind == QueryAntiPatternFindingKind.CountStarVariableExistenceCheck);
    }

    [Fact]
    public void CountStarAssignedThenComparedLessThanZero_NeverFires()
    {
        var findings = Scan(
            "DECLARE @cnt INT; SELECT @cnt = COUNT(*) FROM dbo.Big; IF @cnt < 0 SELECT 1;");

        Assert.DoesNotContain(findings, f => f.Kind == QueryAntiPatternFindingKind.CountStarVariableExistenceCheck);
    }

    [Theory]
    [InlineData("IF 0 > @cnt SELECT 1;")]
    [InlineData("IF 5 <= @cnt SELECT 1;")]
    public void CountStarAssignedThenComparedLiteralFirst_VariousForms_NeverFire(string ifStatement)
    {
        var findings = Scan($"DECLARE @cnt INT; SELECT @cnt = COUNT(*) FROM dbo.Big; {ifStatement}");

        Assert.DoesNotContain(findings, f => f.Kind == QueryAntiPatternFindingKind.CountStarVariableExistenceCheck);
    }

    [Fact]
    public void CountStarAssignedThenComparedLiteralFirstNotEqual_Fires()
    {
        var findings = Scan(
            "DECLARE @cnt INT; SELECT @cnt = COUNT(*) FROM dbo.Big; IF 0 <> @cnt SELECT 1;");

        Assert.Single(findings, f => f.Kind == QueryAntiPatternFindingKind.CountStarVariableExistenceCheck);
    }

    [Fact]
    public void CountStarAssignedThenComparedAgainstAnotherVariable_NeverFires()
    {
        var findings = Scan(
            "DECLARE @cnt INT; DECLARE @other INT = 0; SELECT @cnt = COUNT(*) FROM dbo.Big; IF @cnt > @other SELECT 1;");

        Assert.DoesNotContain(findings, f => f.Kind == QueryAntiPatternFindingKind.CountStarVariableExistenceCheck);
    }

    [Fact]
    public void HavingConditionWithNoColumnReferences_NeverFires()
    {
        var findings = Scan("SELECT Col, COUNT(*) FROM dbo.Big GROUP BY Col HAVING 1 = 1 AND COUNT(*) > 1;");

        Assert.DoesNotContain(findings, f => f.Kind == QueryAntiPatternFindingKind.NonAggregateHavingPredicate);
    }

    [Fact]
    public void HavingConditionReferencesNonGroupByColumn_NeverFires()
    {
        var findings = Scan("SELECT Col, COUNT(*) FROM dbo.Big GROUP BY Col HAVING Id = 1 AND COUNT(*) > 1;");

        Assert.DoesNotContain(findings, f => f.Kind == QueryAntiPatternFindingKind.NonAggregateHavingPredicate);
    }

    [Fact]
    public void SelectDistinctJoinToDerivedTable_NeverFiresDistinctMaskingJoinFanout()
    {
        var findings = Scan(
            "SELECT DISTINCT a.Id FROM dbo.A a JOIN (SELECT AId FROM dbo.C) c ON a.Id = c.AId;");

        Assert.DoesNotContain(findings, f => f.Kind == QueryAntiPatternFindingKind.DistinctMaskingJoinFanout);
    }

    [Fact]
    public void SelectDistinctJoinOnUnresolvableTable_NeverFiresDistinctMaskingJoinFanout()
    {
        var findings = Scan(
            "SELECT DISTINCT a.Id FROM dbo.A a JOIN dbo.NoSuchTable n ON a.Id = n.AId;");

        Assert.DoesNotContain(findings, f => f.Kind == QueryAntiPatternFindingKind.DistinctMaskingJoinFanout);
    }

    [Fact]
    public void SelectDistinctJoinWithNoAliasQualifiedEqualityColumn_NeverFiresDistinctMaskingJoinFanout()
    {
        var findings = Scan(
            "SELECT DISTINCT a.Id FROM dbo.A a JOIN dbo.C c ON 1 = 1;");

        Assert.DoesNotContain(findings, f => f.Kind == QueryAntiPatternFindingKind.DistinctMaskingJoinFanout);
    }

    [Fact]
    public void UnionBranchIsUnflattenableParenthesizedQuery_NeverFiresDisjointness()
    {
        var findings = Scan(
            "SELECT Id FROM dbo.A WHERE Id = 1 "
            + "UNION (SELECT Id FROM dbo.A WHERE Id = 2 UNION ALL SELECT Id FROM dbo.A WHERE Id = 3);");

        Assert.DoesNotContain(findings, f => f.Kind == QueryAntiPatternFindingKind.UnionOfProvablyDisjointBranches);
    }

    [Fact]
    public void UnionBranchesFilterDifferentTables_NeverFiresDisjointness()
    {
        var findings = Scan(
            "SELECT Id FROM dbo.A WHERE Id = 1 UNION SELECT Id FROM dbo.C WHERE AId = 2;");

        Assert.DoesNotContain(findings, f => f.Kind == QueryAntiPatternFindingKind.UnionOfProvablyDisjointBranches);
    }

    [Fact]
    public void UnionBranchHasMultiTableFrom_NeverFiresDisjointness()
    {
        var findings = Scan(
            "SELECT a.Id FROM dbo.A a, dbo.C c WHERE a.Id = 1 UNION SELECT Id FROM dbo.A WHERE Id = 2;");

        Assert.DoesNotContain(findings, f => f.Kind == QueryAntiPatternFindingKind.UnionOfProvablyDisjointBranches);
    }

    [Fact]
    public void UnionBranchUsesNonEqualsComparison_NeverFiresDisjointness()
    {
        var findings = Scan(
            "SELECT Id FROM dbo.A WHERE Id <> 1 UNION SELECT Id FROM dbo.A WHERE Id = 2;");

        Assert.DoesNotContain(findings, f => f.Kind == QueryAntiPatternFindingKind.UnionOfProvablyDisjointBranches);
    }
}
