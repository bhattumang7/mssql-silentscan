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
}
