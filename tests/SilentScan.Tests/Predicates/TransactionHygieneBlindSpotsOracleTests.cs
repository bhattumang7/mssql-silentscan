using Microsoft.Data.SqlClient;
using SilentScan.Core.Parsing;
using SilentScan.Core.Predicates;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.TransactionHygieneImplicitTransactionUnresolvedOnSomePathRuleId)]
public sealed class TransactionHygieneBlindSpotsOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(TransactionHygieneBlindSpotsOracleTests);

    protected override string Ddl => """
        CREATE TABLE dbo.T1 (Id INT NOT NULL PRIMARY KEY);
        GO
        CREATE PROCEDURE dbo.p_explicit_transaction_leak AS
        BEGIN
            BEGIN TRANSACTION;
            UPDATE dbo.T1 SET Id = Id;
            RETURN;
        END
        GO
        CREATE PROCEDURE dbo.p_implicit_transaction_leak AS
        BEGIN
            SET IMPLICIT_TRANSACTIONS ON;
            UPDATE dbo.T1 SET Id = Id;
            RETURN;
        END
        GO
        CREATE PROCEDURE dbo.p_implicit_transaction_committed AS
        BEGIN
            SET IMPLICIT_TRANSACTIONS ON;
            UPDATE dbo.T1 SET Id = Id;
            COMMIT TRANSACTION;
            RETURN;
        END
        GO
        CREATE PROCEDURE dbo.p_implicit_transaction_drained_by_loop AS
        BEGIN
            SET IMPLICIT_TRANSACTIONS ON;
            UPDATE dbo.T1 SET Id = Id;
            WHILE @@TRANCOUNT > 0 COMMIT TRANSACTION;
            RETURN;
        END
        GO
        CREATE PROCEDURE dbo.p_implicit_transaction_no_dml AS
        BEGIN
            SET IMPLICIT_TRANSACTIONS ON;
            DECLARE @x INT = 1;
            SELECT @x;
            RETURN;
        END
        GO
        """;

    private static IReadOnlyList<TransactionHygieneFinding> Scan(string procedureBody)
    {
        var sql = $"CREATE PROCEDURE dbo.p AS\nBEGIN\n{procedureBody}\nEND";
        var result = SqlScriptParser.ParseText("test.sql", sql);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        return TransactionHygieneScanner.Scan(result);
    }

    private new async Task<SqlConnection> OpenConnectionAsync()
    {
        var connection = new SqlConnection(Options.BuildConnectionString(DatabaseName));
        await connection.OpenAsync();
        return connection;
    }

    private static async Task<int> ReadTranCountAsync(SqlConnection connection)
    {
        await using var command = new SqlCommand("SELECT @@TRANCOUNT;", connection);
        return (int)(await command.ExecuteScalarAsync())!;
    }

    private static async Task CleanUpAnyOpenTransactionAsync(SqlConnection connection)
    {
        await using var cleanup = new SqlCommand("IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;", connection);
        await cleanup.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task ImplicitTransactions_UnclosedUpdate_EngineLeavesTranCountElevatedWithNoBeginTransactionInSourceAndNoWarning()
    {
        await using var connection = await OpenConnectionAsync();
        try
        {
            await using var exec = new SqlCommand("EXEC dbo.p_implicit_transaction_leak;", connection);
            await exec.ExecuteNonQueryAsync();

            Assert.Equal(1, await ReadTranCountAsync(connection));
        }
        finally
        {
            await CleanUpAnyOpenTransactionAsync(connection);
        }
    }

    [Fact]
    public async Task ExplicitTransaction_UnclosedUpdate_UnlikeTheImplicitCase_RaisesMsg266WhenTheProcedureReturns()
    {
        await using var connection = await OpenConnectionAsync();
        try
        {
            await using var exec = new SqlCommand("EXEC dbo.p_explicit_transaction_leak;", connection);
            var ex = await Assert.ThrowsAsync<SqlException>(() => exec.ExecuteNonQueryAsync());

            Assert.Equal(266, ex.Number);
        }
        finally
        {
            await CleanUpAnyOpenTransactionAsync(connection);
        }
    }

    [Fact]
    public void ImplicitTransactions_ScannerFlagsTheImplicitlyOpenedTransaction()
    {
        var findings = Scan(
            """
            SET IMPLICIT_TRANSACTIONS ON;
            UPDATE dbo.T1 SET Id = Id;
            RETURN;
            """);

        var finding = Assert.Single(findings);
        Assert.Equal(TransactionHygieneFindingKind.ImplicitTransactionUnresolvedOnSomePath, finding.Kind);
    }

    [Fact]
    public async Task ImplicitTransactions_DrainedByAWhileTranCountLoop_LeavesNothingOpenAndNeverFires()
    {
        await using var connection = await OpenConnectionAsync();

        await using var exec = new SqlCommand("EXEC dbo.p_implicit_transaction_drained_by_loop;", connection);
        await exec.ExecuteNonQueryAsync();

        Assert.Equal(0, await ReadTranCountAsync(connection));

        var findings = Scan(
            """
            SET IMPLICIT_TRANSACTIONS ON;
            UPDATE dbo.T1 SET Id = Id;
            WHILE @@TRANCOUNT > 0 COMMIT TRANSACTION;
            RETURN;
            """);

        Assert.Empty(findings);
    }

    [Fact]
    public async Task ImplicitTransactions_Committed_NeverFires()
    {
        await using var connection = await OpenConnectionAsync();

        await using var exec = new SqlCommand("EXEC dbo.p_implicit_transaction_committed;", connection);
        await exec.ExecuteNonQueryAsync();

        Assert.Equal(0, await ReadTranCountAsync(connection));

        var findings = Scan(
            """
            SET IMPLICIT_TRANSACTIONS ON;
            UPDATE dbo.T1 SET Id = Id;
            COMMIT TRANSACTION;
            RETURN;
            """);

        Assert.Empty(findings);
    }

    [Fact]
    public async Task ImplicitTransactions_NoDmlOnlyDeclareAndFromlessSelect_NeverOpensATransaction_NeverFires()
    {
        await using var connection = await OpenConnectionAsync();

        await using var exec = new SqlCommand("EXEC dbo.p_implicit_transaction_no_dml;", connection);
        await exec.ExecuteNonQueryAsync();

        Assert.Equal(0, await ReadTranCountAsync(connection));

        var findings = Scan(
            """
            SET IMPLICIT_TRANSACTIONS ON;
            DECLARE @x INT = 1;
            SELECT @x;
            RETURN;
            """);

        Assert.Empty(findings);
    }

}
