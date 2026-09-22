using SilentScan.Tests.Support;
using SilentScan.Live.Sweep;

namespace SilentScan.Tests.Sweep;

public sealed class BareStatementHarnessTests
{
    [Fact]
    public void WrapBareStatementsInProcedures_LeadingCommentBeforeCreateProcedure_IsPreserved()
    {
        const string sql = """
            -- Returns the id of every order currently in the Active status.
            CREATE PROCEDURE dbo.GetActiveOrders
            AS
            BEGIN
                SELECT 1;
            END
            """;

        var wrapped = BareStatementHarness.WrapBareStatementsInProcedures(sql);

        Assert.Contains("-- Returns the id of every order currently in the Active status.", wrapped);
    }

    [Fact]
    public void WrapBareStatementsInProcedures_TrailingCommentOnPriorStatement_IsNotAttachedToNextStatement()
    {
        const string sql = """
            CREATE TABLE dbo.T (Id INT NOT NULL); -- trailing note about T
            CREATE PROCEDURE dbo.GetActiveOrders
            AS
            BEGIN
                SELECT 1;
            END
            """;

        var wrapped = BareStatementHarness.WrapBareStatementsInProcedures(sql);
        var procIndex = wrapped.IndexOf("CREATE PROCEDURE", StringComparison.Ordinal);

        Assert.True(procIndex >= 0);
        Assert.DoesNotContain("-- trailing note about T", wrapped[procIndex..]);
    }

    [Fact]
    public void WrapBareStatementsInProcedures_SetStatementBeforeCreateTable_IsNotDeferredIntoAnUnexecutedProcedure()
    {
        const string sql = """
            SET ANSI_PADDING OFF;
            CREATE TABLE dbo.Codes (Code VARCHAR(20) NOT NULL);
            """;

        var wrapped = BareStatementHarness.WrapBareStatementsInProcedures(sql);
        var createTableIndex = wrapped.IndexOf("CREATE TABLE", StringComparison.Ordinal);

        Assert.True(createTableIndex >= 0);
        Assert.DoesNotContain("CREATE PROCEDURE", wrapped[..createTableIndex]);
        Assert.Contains("SET ANSI_PADDING OFF", wrapped[..createTableIndex]);
    }

    [Fact]
    public void WrapBareStatementsInProcedures_BareSelectAfterCreateTable_IsStillDeferredIntoAProcedure()
    {
        const string sql = """
            CREATE TABLE dbo.T (Id INT NOT NULL);
            SELECT Id FROM dbo.T;
            """;

        var wrapped = BareStatementHarness.WrapBareStatementsInProcedures(sql);

        Assert.Contains("CREATE PROCEDURE dbo.__SilentScanHarness_1", wrapped);
    }

    [Fact]
    public void WrapBareStatementsInProcedures_CreateSchemaFollowedByCreateTable_PutsEachInItsOwnBatch()
    {
        const string sql = """
            CREATE SCHEMA Security;
            GO
            CREATE TABLE dbo.T (Id INT NOT NULL);
            """;

        var wrapped = BareStatementHarness.WrapBareStatementsInProcedures(sql);
        var batches = SqlBatchText.SplitBatches(wrapped)
            .Where(b => b.Trim().Length > 0)
            .ToList();

        Assert.Equal(2, batches.Count);
        Assert.Contains("CREATE SCHEMA Security", batches[0]);
        Assert.DoesNotContain("CREATE TABLE", batches[0]);
    }

    [Fact]
    public void WrapBareStatementsInProcedures_CreateFunctionFollowedByCreateTable_PutsEachInItsOwnBatch()
    {
        const string sql = """
            CREATE FUNCTION dbo.f() RETURNS TABLE AS RETURN SELECT 1 AS X;
            GO
            CREATE TABLE dbo.T (Id INT NOT NULL);
            """;

        var wrapped = BareStatementHarness.WrapBareStatementsInProcedures(sql);
        var batches = SqlBatchText.SplitBatches(wrapped)
            .Where(b => b.Trim().Length > 0)
            .ToList();

        Assert.Equal(2, batches.Count);
        Assert.DoesNotContain("CREATE TABLE", batches[0]);
    }

    [Fact]
    public void WrapBareStatementsInProcedures_SetStatementAfterADeclare_StaysInTheSameProcedure()
    {
        var wrapped = BareStatementHarness.WrapBareStatementsInProcedures(
            "DECLARE @id INT;\nSET CURSOR_CLOSE_ON_COMMIT ON;\nSELECT @id;");

        Assert.Equal(1, SqlBatchText.CountCreateProcedure(wrapped));
        Assert.Contains("SET CURSOR_CLOSE_ON_COMMIT ON;", wrapped);
    }

    [Fact]
    public void WrapBareStatementsInProcedures_SetStatementAfterASelectWithNoDeclare_RunsOutsideTheProcedure()
    {
        var wrapped = BareStatementHarness.WrapBareStatementsInProcedures(
            "SELECT 1;\nSET QUOTED_IDENTIFIER OFF;\nGO\nCREATE PROCEDURE dbo.P AS SELECT 2;");

        var setIndex = wrapped.IndexOf("SET QUOTED_IDENTIFIER OFF;", StringComparison.Ordinal);
        var harnessEnd = wrapped.IndexOf("END", StringComparison.Ordinal);

        Assert.True(setIndex > harnessEnd, wrapped);
    }

    [Fact]
    public void WrapBareStatementsInProcedures_MetadataProcedureCall_RunsDirectlyInsteadOfInsideAProcedure()
    {
        var wrapped = BareStatementHarness.WrapBareStatementsInProcedures("EXEC sp_refreshview 'dbo.V';");

        Assert.DoesNotContain("__SilentScanHarness_", wrapped);
        Assert.Contains("EXEC sp_refreshview 'dbo.V';", wrapped);
    }

    [Fact]
    public void WrapBareStatementsInProcedures_OtherProcedureCall_StaysInsideAProcedure()
    {
        var wrapped = BareStatementHarness.WrapBareStatementsInProcedures("EXEC dbo.DoWork;");

        Assert.Contains("CREATE PROCEDURE dbo.__SilentScanHarness_1", wrapped);
    }
}
