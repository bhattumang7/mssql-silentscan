using SilentScan.Core.Catalog;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;

namespace SilentScan.Tests.Predicates;

public sealed class ControlFlowRiskScannerTests
{
    private static IReadOnlyList<ControlFlowRiskFinding> Scan(string sql, DatabaseCatalog? catalog = null)
    {
        var result = SqlScriptParser.ParseText("test.sql", sql);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        return ControlFlowRiskScanner.Scan(result, catalog ?? new DatabaseCatalog());
    }

    [Fact]
    public void EmptyCatchBlock_Fires()
    {
        var findings = Scan("""
            CREATE PROCEDURE dbo.P AS
            BEGIN
                BEGIN TRY
                    SELECT 1;
                END TRY
                BEGIN CATCH
                END CATCH
            END
            """);

        var finding = Assert.Single(findings, f => f.Kind == ControlFlowRiskFindingKind.EmptyCatchBlock);
        Assert.Equal(FindingConfidence.High, finding.Confidence);
    }

    [Fact]
    public void EmptyCatchBlock_ReportsARealLineNotASentinel()
    {

        var findings = Scan("""
            CREATE PROCEDURE dbo.P AS
            BEGIN
                BEGIN TRY
                    SELECT 1;
                END TRY
                BEGIN CATCH
                END CATCH
            END
            """);

        var finding = Assert.Single(findings, f => f.Kind == ControlFlowRiskFindingKind.EmptyCatchBlock);
        Assert.True(finding.Line > 0, $"expected a real, positive line number, got {finding.Line}");
        Assert.True(finding.Column > 0, $"expected a real, positive column number, got {finding.Column}");
    }

    [Fact]
    public void CatchBlockWithStatements_NeverFires()
    {
        var findings = Scan("""
            CREATE PROCEDURE dbo.P AS
            BEGIN
                BEGIN TRY
                    SELECT 1;
                END TRY
                BEGIN CATCH
                    THROW;
                END CATCH
            END
            """);

        Assert.DoesNotContain(findings, f => f.Kind == ControlFlowRiskFindingKind.EmptyCatchBlock);
    }

    [Fact]
    public void SelectInTrigger_Fires()
    {
        var findings = Scan("CREATE TRIGGER dbo.Trg ON dbo.T AFTER INSERT AS BEGIN SELECT * FROM inserted; END");

        var finding = Assert.Single(findings, f => f.Kind == ControlFlowRiskFindingKind.TriggerEmitsOutput);
        Assert.Equal(FindingConfidence.Medium, finding.Confidence);
    }

    [Fact]
    public void PrintInTrigger_Fires()
    {
        var findings = Scan("CREATE TRIGGER dbo.Trg ON dbo.T AFTER INSERT AS BEGIN PRINT 'fired'; END");

        Assert.Contains(findings, f => f.Kind == ControlFlowRiskFindingKind.TriggerEmitsOutput);
    }

    [Fact]
    public void AssignmentOnlySelectInTrigger_NeverFires()
    {
        var findings = Scan("""
            CREATE TRIGGER dbo.Trg ON dbo.T AFTER INSERT AS
            BEGIN
                DECLARE @id INT;
                SELECT @id = Id FROM inserted;
            END
            """);

        Assert.DoesNotContain(findings, f => f.Kind == ControlFlowRiskFindingKind.TriggerEmitsOutput);
    }

    [Fact]
    public void SelectIntoInTrigger_NeverFires()
    {
        var findings = Scan("CREATE TRIGGER dbo.Trg ON dbo.T AFTER INSERT AS BEGIN SELECT * INTO #tmp FROM inserted; END");

        Assert.DoesNotContain(findings, f => f.Kind == ControlFlowRiskFindingKind.TriggerEmitsOutput);
    }

    [Fact]
    public void CursorDefiningSelectInTrigger_NeverFiresTriggerOutput()
    {

        var findings = Scan("""
            CREATE TRIGGER dbo.Trg ON dbo.T AFTER INSERT AS
            BEGIN
                DECLARE cur CURSOR LOCAL FAST_FORWARD FOR SELECT Id FROM inserted;
                OPEN cur;
                CLOSE cur;
                DEALLOCATE cur;
            END
            """);

        Assert.DoesNotContain(findings, f => f.Kind == ControlFlowRiskFindingKind.TriggerEmitsOutput);
    }

    [Fact]
    public void NoLockInsideCursorDefiningSelectInTrigger_StillFiresDirtyRead()
    {

        var findings = Scan("""
            CREATE TRIGGER dbo.Trg ON dbo.T AFTER INSERT AS
            BEGIN
                DECLARE cur CURSOR LOCAL FAST_FORWARD FOR SELECT Id FROM dbo.Other WITH (NOLOCK);
                OPEN cur;
                CLOSE cur;
                DEALLOCATE cur;
            END
            """);

        Assert.Contains(findings, f => f.Kind == ControlFlowRiskFindingKind.DirtyReadIsolationHint);
    }

    [Fact]
    public void SelectInOrdinaryProcedure_NeverFiresTriggerOutput()
    {
        var findings = Scan("CREATE PROCEDURE dbo.P AS BEGIN SELECT 1; END");

        Assert.DoesNotContain(findings, f => f.Kind == ControlFlowRiskFindingKind.TriggerEmitsOutput);
    }

    [Fact]
    public void NoLockHint_Fires()
    {
        var findings = Scan("SELECT A FROM dbo.T WITH (NOLOCK);");

        var finding = Assert.Single(findings, f => f.Kind == ControlFlowRiskFindingKind.DirtyReadIsolationHint);
        Assert.Equal(FindingConfidence.Low, finding.Confidence);
    }

    [Fact]
    public void ReadUncommittedHint_Fires()
    {
        var findings = Scan("SELECT A FROM dbo.T WITH (READUNCOMMITTED);");

        Assert.Contains(findings, f => f.Kind == ControlFlowRiskFindingKind.DirtyReadIsolationHint);
    }

    [Fact]
    public void SetIsolationLevelReadUncommitted_Fires()
    {
        var findings = Scan("SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;");

        Assert.Contains(findings, f => f.Kind == ControlFlowRiskFindingKind.DirtyReadIsolationHint);
    }

    [Fact]
    public void SetIsolationLevelReadCommitted_NeverFires()
    {
        var findings = Scan("SET TRANSACTION ISOLATION LEVEL READ COMMITTED;");

        Assert.DoesNotContain(findings, f => f.Kind == ControlFlowRiskFindingKind.DirtyReadIsolationHint);
    }

    [Fact]
    public void NoTableHint_NeverFiresDirtyRead()
    {
        var findings = Scan("SELECT A FROM dbo.T;");

        Assert.DoesNotContain(findings, f => f.Kind == ControlFlowRiskFindingKind.DirtyReadIsolationHint);
    }

    [Fact]
    public void AtAtIdentityReference_Fires()
    {
        var findings = Scan("""
            CREATE PROCEDURE dbo.P AS
            BEGIN
                INSERT INTO dbo.T (A) VALUES (1);
                SELECT @@IDENTITY;
            END
            """);

        var finding = Assert.Single(findings, f => f.Kind == ControlFlowRiskFindingKind.LegacyIdentityIntrinsic);
        Assert.Equal(FindingConfidence.Medium, finding.Confidence);
    }

    [Fact]
    public void ScopeIdentityReference_NeverFires()
    {
        var findings = Scan("""
            CREATE PROCEDURE dbo.P AS
            BEGIN
                INSERT INTO dbo.T (A) VALUES (1);
                SELECT SCOPE_IDENTITY();
            END
            """);

        Assert.DoesNotContain(findings, f => f.Kind == ControlFlowRiskFindingKind.LegacyIdentityIntrinsic);
    }

    [Fact]
    public void Goto_Fires()
    {
        var findings = Scan("""
            CREATE PROCEDURE dbo.P AS
            BEGIN
                GOTO Done;
                SELECT 1;
                Done:
                SELECT 2;
            END
            """);

        var finding = Assert.Single(findings, f => f.Kind == ControlFlowRiskFindingKind.GotoUsage);
        Assert.Equal(FindingConfidence.High, finding.Confidence);
    }

    [Fact]
    public void NoGoto_NeverFires()
    {
        var findings = Scan("""
            CREATE PROCEDURE dbo.P AS
            BEGIN
                SELECT 1;
            END
            """);

        Assert.DoesNotContain(findings, f => f.Kind == ControlFlowRiskFindingKind.GotoUsage);
    }

    [Fact]
    public void SimpleCaseWithNoElse_Fires()
    {
        var findings = Scan("""
            CREATE PROCEDURE dbo.P AS
            BEGIN
                DECLARE @x INT = 1;
                SELECT CASE @x WHEN 1 THEN 'a' WHEN 2 THEN 'b' END;
            END
            """);

        var finding = Assert.Single(findings, f => f.Kind == ControlFlowRiskFindingKind.CaseExpressionMissingElse);
        Assert.Equal(FindingConfidence.High, finding.Confidence);
    }

    private static DatabaseCatalog CalendarCatalog(string? checkDefinition, bool isNotTrusted = false, bool isDisabled = false)
    {
        var ddl = SqlScriptParser.ParseText("ddl.sql", "CREATE TABLE dbo.Cal (D INT NOT NULL, Flag BIT NOT NULL, Name VARCHAR(10) NOT NULL);");
        var catalog = CatalogBuilder.Build([ddl]);
        if (checkDefinition is not null)
        {
            catalog.AddCheckConstraint(new CatalogCheckConstraint("CK_Cal_D", "dbo.Cal", isNotTrusted, isDisabled, checkDefinition));
        }

        return catalog;
    }

    private static string CalendarCase(string input, string whenValues) =>
        $"CREATE PROCEDURE dbo.P AS BEGIN SELECT CASE {input} {string.Join(" ", whenValues.Split(',').Select(v => $"WHEN {v} THEN 'x'"))} END FROM dbo.Cal; END";

    [Theory]
    [InlineData("DATEPART(dw, GETDATE())", "1,2,3,4,5,6,7")]
    [InlineData("DATEPART(weekday, GETDATE())", "7,6,5,4,3,2,1")]
    [InlineData("DATEPART(QUARTER, GETDATE())", "1,2,3,4")]
    [InlineData("DATEPART(month, GETDATE())", "1,2,3,4,5,6,7,8,9,10,11,12")]
    [InlineData("DATEPART(hour, GETDATE())", "0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20,21,22,23")]
    [InlineData("MONTH(GETDATE())", "1,2,3,4,5,6,7,8,9,10,11,12")]
    [InlineData("Flag", "0,1")]
    public void SimpleCaseWhoseWhenValuesCoverTheInputsWholeDomain_NeverFiresMissingElse(string input, string whenValues)
    {
        var findings = Scan(CalendarCase(input, whenValues), CalendarCatalog(null));

        Assert.DoesNotContain(findings, f => f.Kind == ControlFlowRiskFindingKind.CaseExpressionMissingElse);
    }

    [Theory]
    [InlineData("DATEPART(dw, GETDATE())", "1,2,3,4,5,6")]
    [InlineData("DATEPART(dw, GETDATE())", "1,2,3,4,5,5,7")]
    [InlineData("DATEPART(dw, GETDATE())", "0,1,2,3,4,5,6")]
    [InlineData("DATEPART(dayofyear, GETDATE())", "1,2,3,4,5,6,7")]
    [InlineData("DATEPART(year, GETDATE())", "1,2,3,4,5,6,7")]
    [InlineData("MONTH(GETDATE())", "1,2,3,4,5,6,7,8,9,10,11")]
    [InlineData("Flag", "1")]
    [InlineData("D", "1,2,3,4,5,6,7")]
    public void SimpleCaseWhoseWhenValuesLeaveAnInputValueUnmatched_StillFiresMissingElse(string input, string whenValues)
    {
        var findings = Scan(CalendarCase(input, whenValues), CalendarCatalog(null));

        Assert.Single(findings, f => f.Kind == ControlFlowRiskFindingKind.CaseExpressionMissingElse);
    }

    [Theory]
    [InlineData("([D]>=(1) AND [D]<=(7))", "1,2,3,4,5,6,7")]
    [InlineData("([D]>=(1) AND [D]<=(7))", "7,6,5,4,3,2,1,9")]
    [InlineData("([D]>=(0) AND [D]<=(6))", "0,1,2,3,4,5,6")]
    [InlineData("([D]=(1) OR [D]=(2) OR [D]=(3))", "1,2,3")]
    public void SimpleCaseOverAColumnWhoseTrustedCheckDomainIsCovered_NeverFiresMissingElse(string check, string whenValues)
    {
        var findings = Scan(CalendarCase("D", whenValues), CalendarCatalog(check));

        Assert.DoesNotContain(findings, f => f.Kind == ControlFlowRiskFindingKind.CaseExpressionMissingElse);
    }

    [Theory]
    [InlineData("([D]>=(1) AND [D]<=(7))", "1,2,3,4,5,6", false, false)]
    [InlineData("([D]>=(1) AND [D]<=(7))", "1,2,3,4,5,6,7", true, false)]
    [InlineData("([D]>=(1) AND [D]<=(7))", "1,2,3,4,5,6,7", false, true)]
    [InlineData("([D]>=(1))", "1,2,3,4,5,6,7", false, false)]
    [InlineData("([D]>(0) AND [D]<(8))", "1,2,3,4,5,6", false, false)]
    public void SimpleCaseOverAColumnWhoseCheckDomainIsNotCoveredOrNotTrusted_StillFiresMissingElse(string check, string whenValues, bool isNotTrusted, bool isDisabled)
    {
        var findings = Scan(CalendarCase("D", whenValues), CalendarCatalog(check, isNotTrusted, isDisabled));

        Assert.Single(findings, f => f.Kind == ControlFlowRiskFindingKind.CaseExpressionMissingElse);
    }

    [Fact]
    public void SimpleCaseWithANonLiteralWhenOverACoveredDomain_StillFiresMissingElse()
    {
        var findings = Scan(
            "CREATE PROCEDURE dbo.P AS BEGIN DECLARE @v INT = 7; SELECT CASE D WHEN 1 THEN 'x' WHEN 2 THEN 'x' WHEN @v THEN 'x' END FROM dbo.Cal; END",
            CalendarCatalog("([D]>=(1) AND [D]<=(3))"));

        Assert.Single(findings, f => f.Kind == ControlFlowRiskFindingKind.CaseExpressionMissingElse);
    }

    [Fact]
    public void SimpleCaseWithElse_NeverFires()
    {
        var findings = Scan("""
            CREATE PROCEDURE dbo.P AS
            BEGIN
                DECLARE @x INT = 1;
                SELECT CASE @x WHEN 1 THEN 'a' WHEN 2 THEN 'b' ELSE 'c' END;
            END
            """);

        Assert.DoesNotContain(findings, f => f.Kind == ControlFlowRiskFindingKind.CaseExpressionMissingElse);
    }

    [Fact]
    public void SearchedCaseWithNoElse_NeverFiresMissingElse()
    {

        var findings = Scan("""
            CREATE PROCEDURE dbo.P AS
            BEGIN
                DECLARE @x INT = 1;
                SELECT CASE WHEN @x = 1 THEN 'a' WHEN @x = 2 THEN 'b' END;
            END
            """);

        Assert.DoesNotContain(findings, f => f.Kind == ControlFlowRiskFindingKind.CaseExpressionMissingElse);
    }

    [Fact]
    public void NewIdAsSimpleCaseInput_Fires()
    {
        var findings = Scan("""
            CREATE PROCEDURE dbo.P AS
            BEGIN
                SELECT CASE NEWID()
                    WHEN '00000000-0000-0000-0000-000000000000' THEN 'a'
                    ELSE 'b'
                END;
            END
            """);

        var finding = Assert.Single(findings, f => f.Kind == ControlFlowRiskFindingKind.NonDeterministicCaseInput);
        Assert.Equal(FindingConfidence.High, finding.Confidence);
        Assert.Contains("NEWID", finding.DetailText);
    }

    [Fact]
    public void RandAsSimpleCaseInput_Fires()
    {
        var findings = Scan("""
            CREATE PROCEDURE dbo.P AS
            BEGIN
                SELECT CASE RAND() WHEN 0.5 THEN 'a' ELSE 'b' END;
            END
            """);

        Assert.Contains(findings, f => f.Kind == ControlFlowRiskFindingKind.NonDeterministicCaseInput);
    }

    [Fact]
    public void CryptGenRandomAsSimpleCaseInput_Fires()
    {
        var findings = Scan("""
            CREATE PROCEDURE dbo.P AS
            BEGIN
                SELECT CASE CRYPT_GEN_RANDOM(1) WHEN 0x00 THEN 'a' ELSE 'b' END;
            END
            """);

        Assert.Contains(findings, f => f.Kind == ControlFlowRiskFindingKind.NonDeterministicCaseInput);
    }

    [Fact]
    public void OrdinaryColumnAsSimpleCaseInput_NeverFiresNonDeterministic()
    {
        var findings = Scan("""
            CREATE PROCEDURE dbo.P AS
            BEGIN
                SELECT CASE Status WHEN 1 THEN 'a' ELSE 'b' END FROM dbo.T;
            END
            """);

        Assert.DoesNotContain(findings, f => f.Kind == ControlFlowRiskFindingKind.NonDeterministicCaseInput);
    }

    [Fact]
    public void GetDateAsSimpleCaseInput_NeverFiresNonDeterministic()
    {

        var findings = Scan("""
            CREATE PROCEDURE dbo.P AS
            BEGIN
                SELECT CASE GETDATE() WHEN '2026-01-01' THEN 'a' ELSE 'b' END;
            END
            """);

        Assert.DoesNotContain(findings, f => f.Kind == ControlFlowRiskFindingKind.NonDeterministicCaseInput);
    }
}
