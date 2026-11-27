using SilentScan.Core.Catalog;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;

namespace SilentScan.Tests.Predicates;

public sealed class TriggerCorrectnessScannerTests
{
    private const string Ddl =
        "CREATE TABLE dbo.T (Id INT NOT NULL PRIMARY KEY, Val INT NOT NULL);"
        + "CREATE TABLE dbo.Other (Id INT NOT NULL PRIMARY KEY, Val INT NOT NULL);"
        + "CREATE TABLE dbo.NullableVal (Id INT NOT NULL PRIMARY KEY, Val INT NULL);";

    private static IReadOnlyList<TriggerCorrectnessFinding> Scan(string triggerSql)
    {
        var result = SqlScriptParser.ParseText("test.sql", $"{Ddl}\nGO\n{triggerSql}");
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));

        var catalog = CatalogBuilder.Build([result]);
        return TriggerCorrectnessScanner.Scan(result, catalog);
    }

    [Fact]
    public void SelectSetVariableFromInserted_NoWhereNoTopNoAggregate_Fires()
    {
        var findings = Scan(
            "CREATE TRIGGER dbo.trg_T ON dbo.T AFTER UPDATE AS "
            + "BEGIN DECLARE @v INT; SELECT @v = Val FROM inserted; PRINT @v; END;");

        var finding = Assert.Single(findings, f => f.Kind == TriggerCorrectnessFindingKind.MultiRowUnsafeSingleRowAssignment);
        Assert.Equal("dbo.trg_T", finding.TriggerQualifiedName);
        Assert.Equal(FindingConfidence.High, finding.Confidence);
    }

    [Fact]
    public void ScalarSubqueryOverDeleted_RaisesAtRunTime_NeverFires()
    {
        var findings = Scan(
            "CREATE TRIGGER dbo.trg_T ON dbo.T AFTER DELETE AS "
            + "BEGIN DECLARE @v INT = (SELECT Val FROM deleted); PRINT @v; END;");

        Assert.DoesNotContain(findings, f => f.Kind == TriggerCorrectnessFindingKind.MultiRowUnsafeSingleRowAssignment);
    }

    [Theory]
    [InlineData("IF (SELECT COUNT(*) FROM inserted) > 1 RETURN;")]
    [InlineData("IF (SELECT COUNT(*) FROM inserted) <> 1 BEGIN ROLLBACK TRANSACTION; RETURN; END;")]
    [InlineData("IF @@ROWCOUNT > 1 RETURN;")]
    [InlineData("IF @@ROWCOUNT <> 1 BEGIN RAISERROR('one row only', 16, 1); RETURN; END;")]
    [InlineData("IF (SELECT COUNT(*) FROM inserted) > 1 THROW 50001, 'one row only', 1;")]
    public void AssignmentAfterAGuardThatExitsOnAMultiRowStatement_NeverFires(string guard)
    {
        var findings = Scan(
            "CREATE TRIGGER dbo.trg_T ON dbo.T AFTER INSERT AS "
            + $"BEGIN {guard} DECLARE @v INT; SELECT @v = Val FROM inserted; PRINT @v; END;");

        Assert.DoesNotContain(findings, f => f.Kind is TriggerCorrectnessFindingKind.MultiRowUnsafeSingleRowAssignment or TriggerCorrectnessFindingKind.MultiRowUnsafeKeyedDml);
    }

    [Theory]
    [InlineData("IF (SELECT COUNT(*) FROM inserted) > 1 PRINT 'many';")]
    [InlineData("IF @@ROWCOUNT = 0 RETURN;")]
    [InlineData("IF (SELECT COUNT(*) FROM dbo.Other) > 1 RETURN;")]
    [InlineData("IF @@ROWCOUNT > 1 RAISERROR('one row only', 16, 1);")]
    [InlineData("IF @@ROWCOUNT > 1 RAISERROR('one row only', 10, 1);")]
    public void AssignmentAfterAnUnrelatedOrNonExitingCheck_StillFires(string check)
    {
        var findings = Scan(
            "CREATE TRIGGER dbo.trg_T ON dbo.T AFTER INSERT AS "
            + $"BEGIN {check} DECLARE @v INT; SELECT @v = Val FROM inserted; PRINT @v; END;");

        Assert.Single(findings, f => f.Kind == TriggerCorrectnessFindingKind.MultiRowUnsafeSingleRowAssignment);
    }

    [Theory]
    [InlineData("SET NOCOUNT ON; IF @@ROWCOUNT > 1 RETURN;", true)]
    [InlineData("DECLARE @n INT = 1; IF @@ROWCOUNT > 1 RETURN;", true)]
    [InlineData("DECLARE @n INT; SET @n = 1; IF @@ROWCOUNT > 1 RETURN;", true)]
    [InlineData("DECLARE @n INT; IF @@ROWCOUNT > 1 RETURN;", false)]
    [InlineData("SET NOCOUNT ON; IF (SELECT COUNT(*) FROM inserted) > 1 RETURN;", false)]
    public void RowCountGuard_OnlyProtectsWhenNothingBeforeItResetsRowCount(string prefix, bool fires)
    {
        var findings = Scan(
            "CREATE TRIGGER dbo.trg_T ON dbo.T AFTER INSERT AS "
            + $"BEGIN {prefix} DECLARE @v INT; SELECT @v = Val FROM inserted; PRINT @v; END;");

        Assert.Equal(fires, findings.Any(f => f.Kind == TriggerCorrectnessFindingKind.MultiRowUnsafeSingleRowAssignment));
    }

    [Theory]
    [InlineData("SELECT @v = ISNULL(MAX(Val), 0) FROM inserted;", false)]
    [InlineData("SELECT @v = MAX(Val) + 1 FROM inserted;", false)]
    [InlineData("SELECT @v = CAST(COUNT(*) AS INT) FROM inserted;", false)]
    [InlineData("SET @v = (SELECT ISNULL(SUM(Val), 0) FROM inserted);", false)]
    [InlineData("SELECT @v = Val + 1 FROM inserted;", true)]
    [InlineData("SELECT @v = ISNULL(Val, 0) FROM inserted;", true)]
    public void SingleRowAggregateWrappedInAnExpression_NeverFires(string assignment, bool fires)
    {
        var findings = Scan(
            "CREATE TRIGGER dbo.trg_T ON dbo.T AFTER INSERT AS "
            + $"BEGIN DECLARE @v INT; {assignment} PRINT @v; END;");

        Assert.Equal(fires, findings.Any(f => f.Kind == TriggerCorrectnessFindingKind.MultiRowUnsafeSingleRowAssignment));
    }

    [Fact]
    public void SelectSetVariableFromInserted_WithWhere_NeverFires()
    {

        var findings = Scan(
            "CREATE TRIGGER dbo.trg_T ON dbo.T AFTER UPDATE AS "
            + "BEGIN DECLARE @v INT; SELECT @v = Val FROM inserted WHERE Id = 1; PRINT @v; END;");

        Assert.DoesNotContain(findings, f => f.Kind is TriggerCorrectnessFindingKind.MultiRowUnsafeSingleRowAssignment or TriggerCorrectnessFindingKind.MultiRowUnsafeKeyedDml);
    }

    [Fact]
    public void SelectSetVariableFromInserted_AggregateExpression_NeverFires()
    {

        var findings = Scan(
            "CREATE TRIGGER dbo.trg_T ON dbo.T AFTER UPDATE AS "
            + "BEGIN DECLARE @v INT; SELECT @v = COUNT(*) FROM inserted; PRINT @v; END;");

        Assert.DoesNotContain(findings, f => f.Kind is TriggerCorrectnessFindingKind.MultiRowUnsafeSingleRowAssignment or TriggerCorrectnessFindingKind.MultiRowUnsafeKeyedDml);
    }

    [Fact]
    public void UnsafeAssignmentThenStraightLineKeyedUpdate_Fires_SharperKind()
    {
        var findings = Scan(
            "CREATE TRIGGER dbo.trg_T ON dbo.T AFTER UPDATE AS "
            + "BEGIN "
            + "DECLARE @v INT; "
            + "SELECT @v = Val FROM inserted; "
            + "UPDATE dbo.Other SET Val = @v WHERE Id = @v; "
            + "END;");

        var finding = Assert.Single(findings, f => f.Kind == TriggerCorrectnessFindingKind.MultiRowUnsafeKeyedDml);
        Assert.Equal(FindingConfidence.High, finding.Confidence);

        Assert.DoesNotContain(findings, f => f.Kind == TriggerCorrectnessFindingKind.MultiRowUnsafeSingleRowAssignment);
    }

    [Fact]
    public void UnsafeAssignmentThenStraightLineKeyedUpdate_ParenthesizedWhere_Fires_SharperKind()
    {
        var findings = Scan(
            "CREATE TRIGGER dbo.trg_T ON dbo.T AFTER UPDATE AS "
            + "BEGIN "
            + "DECLARE @v INT; "
            + "SELECT @v = Val FROM inserted; "
            + "UPDATE dbo.Other SET Val = @v WHERE (Id = @v); "
            + "END;");

        var finding = Assert.Single(findings, f => f.Kind == TriggerCorrectnessFindingKind.MultiRowUnsafeKeyedDml);
        Assert.Equal(FindingConfidence.High, finding.Confidence);

        Assert.DoesNotContain(findings, f => f.Kind == TriggerCorrectnessFindingKind.MultiRowUnsafeSingleRowAssignment);
    }

    [Fact]
    public void UnsafeAssignmentWithNoSubsequentKeyedUse_Fires_GeneralKindOnly()
    {
        var findings = Scan(
            "CREATE TRIGGER dbo.trg_T ON dbo.T AFTER UPDATE AS "
            + "BEGIN "
            + "DECLARE @v INT; "
            + "SELECT @v = Val FROM inserted; "
            + "UPDATE dbo.Other SET Val = 0 WHERE Id = 1; "
            + "END;");

        Assert.Single(findings, f => f.Kind == TriggerCorrectnessFindingKind.MultiRowUnsafeSingleRowAssignment);
        Assert.DoesNotContain(findings, f => f.Kind == TriggerCorrectnessFindingKind.MultiRowUnsafeKeyedDml);
    }

    [Fact]
    public void InsteadOfInsertTrigger_ReinsertsFilteredSubsetWithNoCompanionOrErrorPath_Fires()
    {
        var findings = Scan(
            "CREATE TRIGGER dbo.trg_T ON dbo.T INSTEAD OF INSERT AS "
            + "BEGIN INSERT INTO dbo.T (Id, Val) SELECT Id, Val FROM inserted WHERE Val > 0; END;");

        var finding = Assert.Single(findings, f => f.Kind == TriggerCorrectnessFindingKind.InsteadOfInsertFilteredNoRejectPath);
        Assert.Equal(FindingConfidence.High, finding.Confidence);
    }

    [Theory]
    [InlineData("Val IS NOT NULL", false)]
    [InlineData("Id IS NOT NULL AND Val IS NOT NULL", false)]
    [InlineData("Val IS NOT NULL AND Val > 0", true)]
    [InlineData("1 = 1", false)]
    [InlineData("Val > 0", true)]
    public void InsteadOfInsertTrigger_SingleReinsert_FiresOnlyWhenTheFilterCanDropARow(string filter, bool fires)
    {
        var findings = Scan(
            "CREATE TRIGGER dbo.trg_T ON dbo.T INSTEAD OF INSERT AS "
            + $"BEGIN INSERT INTO dbo.T (Id, Val) SELECT Id, Val FROM inserted WHERE {filter}; END;");

        Assert.Equal(fires, findings.Any(f => f.Kind == TriggerCorrectnessFindingKind.InsteadOfInsertFilteredNoRejectPath));
    }

    [Fact]
    public void InsteadOfInsertTrigger_FilteredReinsertWithCompanionInsert_NeverFires()
    {
        var findings = Scan(
            "CREATE TRIGGER dbo.trg_T ON dbo.T INSTEAD OF INSERT AS "
            + "BEGIN "
            + "INSERT INTO dbo.T (Id, Val) SELECT Id, Val FROM inserted WHERE Val > 0; "
            + "INSERT INTO dbo.Other (Id, Val) SELECT Id, Val FROM inserted WHERE Val <= 0; "
            + "END;");

        Assert.DoesNotContain(findings, f => f.Kind == TriggerCorrectnessFindingKind.InsteadOfInsertFilteredNoRejectPath);
    }

    [Fact]
    public void InsteadOfInsertTrigger_FilteredReinsertWithRaiserror_NeverFires()
    {
        var findings = Scan(
            "CREATE TRIGGER dbo.trg_T ON dbo.T INSTEAD OF INSERT AS "
            + "BEGIN "
            + "INSERT INTO dbo.T (Id, Val) SELECT Id, Val FROM inserted WHERE Val > 0; "
            + "IF EXISTS (SELECT 1 FROM inserted WHERE Val <= 0) RAISERROR('rejected rows', 16, 1); "
            + "END;");

        Assert.DoesNotContain(findings, f => f.Kind == TriggerCorrectnessFindingKind.InsteadOfInsertFilteredNoRejectPath);
    }

    [Fact]
    public void InsteadOfInsertTrigger_UnfilteredReinsertOfAllInsertedRows_NeverFires()
    {
        var findings = Scan(
            "CREATE TRIGGER dbo.trg_T ON dbo.T INSTEAD OF INSERT AS "
            + "BEGIN INSERT INTO dbo.T (Id, Val) SELECT Id, Val FROM inserted; END;");

        Assert.DoesNotContain(findings, f => f.Kind == TriggerCorrectnessFindingKind.InsteadOfInsertFilteredNoRejectPath);
    }

    [Fact]
    public void InsteadOfInsertTrigger_ExactComplementGreaterThanLessOrEqual_NotNullColumn_NeverFires()
    {
        var findings = Scan(
            "CREATE TRIGGER dbo.trg_T ON dbo.T INSTEAD OF INSERT AS "
            + "BEGIN "
            + "INSERT INTO dbo.T (Id, Val) SELECT Id, Val FROM inserted WHERE Val > 0; "
            + "INSERT INTO dbo.Other (Id, Val) SELECT Id, Val FROM inserted WHERE Val <= 0; "
            + "END;");

        Assert.DoesNotContain(findings, f => f.Kind == TriggerCorrectnessFindingKind.InsteadOfInsertFilteredNoRejectPath);
    }

    [Fact]
    public void InsteadOfInsertTrigger_ExactComplementGreaterOrEqualLessThan_NotNullColumn_NeverFires()
    {
        var findings = Scan(
            "CREATE TRIGGER dbo.trg_T ON dbo.T INSTEAD OF INSERT AS "
            + "BEGIN "
            + "INSERT INTO dbo.T (Id, Val) SELECT Id, Val FROM inserted WHERE Val >= 0; "
            + "INSERT INTO dbo.Other (Id, Val) SELECT Id, Val FROM inserted WHERE Val < 0; "
            + "END;");

        Assert.DoesNotContain(findings, f => f.Kind == TriggerCorrectnessFindingKind.InsteadOfInsertFilteredNoRejectPath);
    }

    [Fact]
    public void InsteadOfInsertTrigger_ExactComplementEqualsNotEquals_NotNullColumn_NeverFires()
    {
        var findings = Scan(
            "CREATE TRIGGER dbo.trg_T ON dbo.T INSTEAD OF INSERT AS "
            + "BEGIN "
            + "INSERT INTO dbo.T (Id, Val) SELECT Id, Val FROM inserted WHERE Val = 0; "
            + "INSERT INTO dbo.Other (Id, Val) SELECT Id, Val FROM inserted WHERE Val <> 0; "
            + "END;");

        Assert.DoesNotContain(findings, f => f.Kind == TriggerCorrectnessFindingKind.InsteadOfInsertFilteredNoRejectPath);
    }

    [Fact]
    public void InsteadOfInsertTrigger_IsNullIsNotNullComplement_NullableColumn_NeverFires()
    {
        var findings = Scan(
            "CREATE TRIGGER dbo.trg_NV ON dbo.NullableVal INSTEAD OF INSERT AS "
            + "BEGIN "
            + "INSERT INTO dbo.NullableVal (Id, Val) SELECT Id, Val FROM inserted WHERE Val IS NULL; "
            + "INSERT INTO dbo.Other (Id, Val) SELECT Id, 0 FROM inserted WHERE Val IS NOT NULL; "
            + "END;");

        Assert.DoesNotContain(findings, f => f.Kind == TriggerCorrectnessFindingKind.InsteadOfInsertFilteredNoRejectPath);
    }

    [Fact]
    public void InsteadOfInsertTrigger_GreaterThanLessOrEqualPair_NullableColumn_StillFires()
    {
        var findings = Scan(
            "CREATE TRIGGER dbo.trg_NV ON dbo.NullableVal INSTEAD OF INSERT AS "
            + "BEGIN "
            + "INSERT INTO dbo.NullableVal (Id, Val) SELECT Id, Val FROM inserted WHERE Val > 0; "
            + "INSERT INTO dbo.Other (Id, Val) SELECT Id, Val FROM inserted WHERE Val <= 0; "
            + "END;");

        Assert.Equal(2, findings.Count(f => f.Kind == TriggerCorrectnessFindingKind.InsteadOfInsertFilteredNoRejectPath));
    }

    [Fact]
    public void InsteadOfInsertTrigger_OverlappingNonComplementaryFilters_NotNullColumn_StillFires()
    {
        var findings = Scan(
            "CREATE TRIGGER dbo.trg_T ON dbo.T INSTEAD OF INSERT AS "
            + "BEGIN "
            + "INSERT INTO dbo.T (Id, Val) SELECT Id, Val FROM inserted WHERE Val > 0; "
            + "INSERT INTO dbo.Other (Id, Val) SELECT Id, Val FROM inserted WHERE Val > 10; "
            + "END;");

        Assert.Equal(2, findings.Count(f => f.Kind == TriggerCorrectnessFindingKind.InsteadOfInsertFilteredNoRejectPath));
    }

    [Fact]
    public void InsteadOfInsertTrigger_ThreeWayNonAdjacentRangeGap_NotNullColumn_StillFires()
    {
        var findings = Scan(
            "CREATE TRIGGER dbo.trg_T ON dbo.T INSTEAD OF INSERT AS "
            + "BEGIN "
            + "INSERT INTO dbo.T (Id, Val) SELECT Id, Val FROM inserted WHERE Val < 0; "
            + "INSERT INTO dbo.Other (Id, Val) SELECT Id, Val FROM inserted WHERE Val = 0; "
            + "INSERT INTO dbo.Other (Id, Val) SELECT Id, Val FROM inserted WHERE Val > 5; "
            + "END;");

        Assert.Equal(3, findings.Count(f => f.Kind == TriggerCorrectnessFindingKind.InsteadOfInsertFilteredNoRejectPath));
    }

    [Fact]
    public void InsteadOfInsertTrigger_ComplementPairWithOneJoinFilteredLeg_StillFires()
    {
        var findings = Scan(
            "CREATE TRIGGER dbo.trg_T ON dbo.T INSTEAD OF INSERT AS "
            + "BEGIN "
            + "INSERT INTO dbo.T (Id, Val) SELECT i.Id, i.Val FROM inserted i INNER JOIN dbo.Other o ON i.Id = o.Id WHERE i.Val > 0; "
            + "INSERT INTO dbo.Other (Id, Val) SELECT Id, Val FROM inserted WHERE Val <= 0; "
            + "END;");

        Assert.Equal(2, findings.Count(f => f.Kind == TriggerCorrectnessFindingKind.InsteadOfInsertFilteredNoRejectPath));
    }

    [Fact]
    public void UpdateFunctionGate_NoValueComparison_Fires()
    {
        var findings = Scan(
            "CREATE TRIGGER dbo.trg_T ON dbo.T AFTER UPDATE AS "
            + "BEGIN IF UPDATE(Val) UPDATE dbo.Other SET Val = 1 WHERE Id = 1; END;");

        var finding = Assert.Single(findings, f => f.Kind == TriggerCorrectnessFindingKind.UpdateFunctionWithoutValueComparison);
        Assert.Equal(FindingConfidence.High, finding.Confidence);
        Assert.Contains("Val", finding.DetailText);
    }

    [Theory]
    [InlineData("AFTER INSERT", false)]
    [InlineData("FOR INSERT", false)]
    [InlineData("INSTEAD OF INSERT", false)]
    [InlineData("AFTER UPDATE", true)]
    [InlineData("AFTER INSERT, UPDATE", true)]
    [InlineData("AFTER UPDATE, DELETE", true)]
    public void UpdateFunctionGate_FiresOnlyWhenTheTriggerCanSeeAnUpdate(string actions, bool fires)
    {
        var findings = Scan(
            $"CREATE TRIGGER dbo.trg_T ON dbo.T {actions} AS "
            + "BEGIN IF UPDATE(Val) UPDATE dbo.Other SET Val = 1 WHERE Id = 1; END;");

        Assert.Equal(fires, findings.Any(f => f.Kind == TriggerCorrectnessFindingKind.UpdateFunctionWithoutValueComparison));
    }

    [Fact]
    public void UpdateFunctionGate_WithSameColumnInsertedDeletedComparison_NeverFires()
    {
        var findings = Scan(
            "CREATE TRIGGER dbo.trg_T ON dbo.T AFTER UPDATE AS "
            + "BEGIN "
            + "IF UPDATE(Val) AND EXISTS (SELECT 1 FROM inserted i INNER JOIN deleted d ON i.Id = d.Id WHERE i.Val <> d.Val) "
            + "UPDATE dbo.Other SET Val = 1 WHERE Id = 1; "
            + "END;");

        Assert.DoesNotContain(findings, f => f.Kind == TriggerCorrectnessFindingKind.UpdateFunctionWithoutValueComparison);
    }

    [Fact]
    public void UpdateFunctionGate_WithSameColumnComparisonFilteringTheGatedBranch_NeverFires()
    {
        var findings = Scan(
            "CREATE TRIGGER dbo.trg_T ON dbo.T AFTER UPDATE AS "
            + "BEGIN "
            + "IF UPDATE(Val) "
            + "BEGIN INSERT INTO dbo.Other (Id, Val) SELECT i.Id, i.Val FROM inserted i INNER JOIN deleted d ON i.Id = d.Id WHERE i.Val <> d.Val; END; "
            + "END;");

        Assert.DoesNotContain(findings, f => f.Kind == TriggerCorrectnessFindingKind.UpdateFunctionWithoutValueComparison);
    }

    [Theory]
    [InlineData("SET @v = (SELECT Val FROM inserted);")]
    [InlineData("SET @v = (SELECT Val FROM inserted); UPDATE dbo.Other SET Val = 1 WHERE Id = @v;")]
    public void SetFromAScalarSubqueryOverInserted_NeverFires(string body)
    {
        var findings = Scan(
            "CREATE TRIGGER dbo.trg_T ON dbo.T AFTER INSERT AS "
            + $"BEGIN DECLARE @v INT; {body} PRINT @v; END;");

        Assert.DoesNotContain(findings, f => f.Kind is TriggerCorrectnessFindingKind.MultiRowUnsafeSingleRowAssignment or TriggerCorrectnessFindingKind.MultiRowUnsafeKeyedDml);
    }

    [Fact]
    public void InsteadOfInsertTrigger_RollingBackRejectedRows_NeverFires()
    {
        var findings = Scan(
            "CREATE TRIGGER dbo.trg_T ON dbo.T INSTEAD OF INSERT AS "
            + "BEGIN IF EXISTS (SELECT 1 FROM inserted WHERE Val <= 0) BEGIN ROLLBACK TRANSACTION; RETURN; END; "
            + "INSERT INTO dbo.T (Id, Val) SELECT Id, Val FROM inserted WHERE Val > 0; END;");

        Assert.DoesNotContain(findings, f => f.Kind == TriggerCorrectnessFindingKind.InsteadOfInsertFilteredNoRejectPath);
    }

    [Theory]
    [InlineData("SELECT @v = (SELECT MAX(Val) FROM dbo.Other) FROM inserted;")]
    [InlineData("SELECT @v = 1 FROM inserted;")]
    [InlineData("SELECT @v = @seed + 1 FROM inserted;")]
    public void AssignmentOfAValueThatIsTheSameForEveryRow_NeverFires(string assignment)
    {
        Assert.Empty(Scan(
            "CREATE TRIGGER dbo.trg_T ON dbo.T AFTER INSERT AS "
            + $"BEGIN DECLARE @v INT; DECLARE @seed INT = 1; {assignment} PRINT @v; END;"));
    }

    [Theory]
    [InlineData("SELECT @v = (SELECT MAX(o.Val) FROM dbo.Other AS o WHERE o.Id = inserted.Id) FROM inserted;")]
    [InlineData("SELECT @v = i.Val + 1 FROM inserted AS i;")]
    public void AssignmentOfAValueThatDependsOnTheRow_StillFires(string assignment)
    {
        Assert.Single(
            Scan("CREATE TRIGGER dbo.trg_T ON dbo.T AFTER INSERT AS " + $"BEGIN DECLARE @v INT; {assignment} PRINT @v; END;"),
            f => f.Kind == TriggerCorrectnessFindingKind.MultiRowUnsafeSingleRowAssignment);
    }

    [Theory]
    [InlineData("INSERT dbo.T (Id, Val) SELECT TOP (1) Id, Val FROM inserted;", true)]
    [InlineData("INSERT dbo.T (Id, Val) SELECT TOP (100) PERCENT Id, Val FROM inserted;", false)]
    [InlineData("INSERT dbo.T (Id, Val) SELECT Id, Val FROM inserted WHERE Id > 1; RAISERROR('x', 10, 1);", true)]
    [InlineData("INSERT dbo.T (Id, Val) SELECT Id, Val FROM inserted WHERE Id > 1; RAISERROR('x', 16, 1);", false)]
    [InlineData("INSERT dbo.T (Id, Val) SELECT Id, Val FROM inserted WHERE Id > 1; INSERT dbo.Log (Id) VALUES (1);", true)]
    [InlineData("INSERT dbo.T (Id, Val) SELECT Id, Val FROM inserted WHERE Id > 1; INSERT dbo.Log (Id) SELECT Id FROM inserted WHERE Id <= 1;", false)]
    [InlineData("SELECT Id INTO #rejected FROM inserted WHERE Id <= 1; INSERT dbo.T (Id, Val) SELECT Id, Val FROM inserted WHERE Id > 1; INSERT dbo.Log (Id) SELECT Id FROM #rejected;", false)]
    public void AFilteredReinsertNeedsAPathThatHandlesTheExcludedRows(string body, bool fires)
    {
        var findings = Scan($"CREATE TRIGGER dbo.trg_T ON dbo.T INSTEAD OF INSERT AS BEGIN {body} END;");

        Assert.Equal(fires, findings.Any(f => f.Kind == TriggerCorrectnessFindingKind.InsteadOfInsertFilteredNoRejectPath));
    }

    [Theory]
    [InlineData("SELECT i.Id FROM inserted i JOIN dbo.T a ON a.Id = i.Id JOIN dbo.T b ON a.Val = b.Val;", true)]
    [InlineData("SELECT i.Id FROM inserted i JOIN deleted d ON d.Id = i.Id WHERE d.Val <> i.Val;", false)]
    public void ABaseTableToBaseTableComparisonIsNotAValueComparison(string body, bool fires)
    {
        var findings = Scan($"CREATE TRIGGER dbo.trg_T ON dbo.T AFTER UPDATE AS BEGIN IF UPDATE(Val) BEGIN {body} END END;");

        Assert.Equal(fires, findings.Any(f => f.Kind == TriggerCorrectnessFindingKind.UpdateFunctionWithoutValueComparison));
    }
}
