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
    public void ScalarSubqueryOverDeleted_NoWhereNoTopNoAggregate_Fires()
    {
        var findings = Scan(
            "CREATE TRIGGER dbo.trg_T ON dbo.T AFTER DELETE AS "
            + "BEGIN DECLARE @v INT = (SELECT Val FROM deleted); PRINT @v; END;");

        Assert.Single(findings, f => f.Kind == TriggerCorrectnessFindingKind.MultiRowUnsafeSingleRowAssignment);
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
    public void AssignmentAfterAnUnrelatedOrNonExitingCheck_StillFires(string check)
    {
        var findings = Scan(
            "CREATE TRIGGER dbo.trg_T ON dbo.T AFTER INSERT AS "
            + $"BEGIN {check} DECLARE @v INT; SELECT @v = Val FROM inserted; PRINT @v; END;");

        Assert.Single(findings, f => f.Kind == TriggerCorrectnessFindingKind.MultiRowUnsafeSingleRowAssignment);
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

    [Fact]
    public void InsteadOfInsertTrigger_FilteredReinsertWithCompanionInsert_NeverFires()
    {
        var findings = Scan(
            "CREATE TRIGGER dbo.trg_T ON dbo.T INSTEAD OF INSERT AS "
            + "BEGIN "
            + "INSERT INTO dbo.T (Id, Val) SELECT Id, Val FROM inserted WHERE Val > 0; "
            + "INSERT INTO dbo.Other (Id, Val) VALUES (-1, -1); "
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
}
