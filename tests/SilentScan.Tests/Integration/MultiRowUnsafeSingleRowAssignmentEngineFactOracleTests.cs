using Microsoft.Data.SqlClient;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.TriggerCorrectnessMultiRowUnsafeSingleRowAssignmentRuleId)]
public sealed class MultiRowUnsafeSingleRowAssignmentEngineFactOracleTests : OracleTestFixture
{
    private static readonly int[] PossibleCapturedValues = [10, 20, 30];

    protected override string DatabaseNameSeed => nameof(MultiRowUnsafeSingleRowAssignmentEngineFactOracleTests);

    protected override string Ddl => """
        CREATE TABLE dbo.T4 (Id INT NOT NULL, Val INT NOT NULL);
        GO
        CREATE TABLE dbo.T4Log (CapturedVal INT NOT NULL);
        GO
        CREATE TRIGGER dbo.trg_T4_Insert ON dbo.T4 AFTER INSERT AS
        BEGIN
            DECLARE @v INT;
            SELECT @v = Val FROM inserted;
            INSERT INTO dbo.T4Log (CapturedVal) VALUES (@v);
        END;
        GO
        CREATE TABLE dbo.AggLog (Captured INT NOT NULL);
        GO
        CREATE TABLE dbo.AggT (Id INT NOT NULL, Val INT NOT NULL);
        GO
        CREATE TRIGGER dbo.trg_AggT ON dbo.AggT AFTER INSERT AS
        BEGIN
            DECLARE @v INT;
            SELECT @v = ISNULL(MAX(Val), 0) + 1 FROM inserted;
            INSERT INTO dbo.AggLog (Captured) VALUES (@v);
        END;
        GO
        CREATE TABLE dbo.RcLog (Prefix VARCHAR(20) NOT NULL, Seen INT NOT NULL);
        GO
        CREATE TABLE dbo.RcNoCount (Id INT NOT NULL);
        GO
        CREATE TRIGGER dbo.trg_RcNoCount ON dbo.RcNoCount AFTER INSERT AS
        BEGIN
            SET NOCOUNT ON;
            INSERT INTO dbo.RcLog (Prefix, Seen) VALUES ('set-nocount', @@ROWCOUNT);
        END;
        GO
        CREATE TABLE dbo.RcDeclare (Id INT NOT NULL);
        GO
        CREATE TRIGGER dbo.trg_RcDeclare ON dbo.RcDeclare AFTER INSERT AS
        BEGIN
            DECLARE @n INT;
            INSERT INTO dbo.RcLog (Prefix, Seen) VALUES ('bare-declare', @@ROWCOUNT);
        END;
        GO
        CREATE TABLE dbo.RcInit (Id INT NOT NULL);
        GO
        CREATE TRIGGER dbo.trg_RcInit ON dbo.RcInit AFTER INSERT AS
        BEGIN
            DECLARE @n INT = 1;
            INSERT INTO dbo.RcLog (Prefix, Seen) VALUES ('init-declare', @@ROWCOUNT);
        END;
        GO
        CREATE TABLE dbo.T5 (Id INT NOT NULL, Val INT NOT NULL);
        GO
        CREATE TABLE dbo.T5Log (CapturedVal INT NOT NULL);
        GO
        CREATE TRIGGER dbo.trg_T5_Insert ON dbo.T5 AFTER INSERT AS
        BEGIN
            IF (SELECT COUNT(*) FROM inserted) > 1 RETURN;
            DECLARE @v INT;
            SELECT @v = Val FROM inserted;
            INSERT INTO dbo.T5Log (CapturedVal) VALUES (@v);
        END;
        GO
        """;

    [Fact]
    public async Task ScalarVariableAssignedFromMultiRowInsert_CapturesOnlyOneArbitraryRowWithNoError()
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(DatabaseName));
        await connection.OpenAsync();

        await using var insertCommand = connection.CreateCommand();
        insertCommand.CommandText = "INSERT INTO dbo.T4 (Id, Val) VALUES (1, 10), (2, 20), (3, 30);";
        await insertCommand.ExecuteNonQueryAsync();

        await using var logCountCommand = connection.CreateCommand();
        logCountCommand.CommandText = "SELECT COUNT(*) FROM dbo.T4Log;";
        var logRowCount = (int)(await logCountCommand.ExecuteScalarAsync())!;
        Assert.Equal(1, logRowCount);

        await using var capturedValCommand = connection.CreateCommand();
        capturedValCommand.CommandText = "SELECT CapturedVal FROM dbo.T4Log;";
        var capturedVal = (int)(await capturedValCommand.ExecuteScalarAsync())!;
        Assert.Contains(capturedVal, PossibleCapturedValues);
    }

    [Fact]
    public async Task GuardThatReturnsOnAMultiRowStatement_LeavesNoArbitraryCapture_SingleRowStillCaptured()
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(DatabaseName));
        await connection.OpenAsync();

        await using var multi = connection.CreateCommand();
        multi.CommandText = "INSERT INTO dbo.T5 (Id, Val) VALUES (1, 10), (2, 20), (3, 30); SELECT COUNT(*) FROM dbo.T5Log;";
        Assert.Equal(0, (int)(await multi.ExecuteScalarAsync())!);

        await using var single = connection.CreateCommand();
        single.CommandText = "INSERT INTO dbo.T5 (Id, Val) VALUES (4, 40); SELECT CapturedVal FROM dbo.T5Log;";
        Assert.Equal(40, (int)(await single.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task RowCountInATrigger_SurvivesOnlyABareDeclare()
    {
        await ExecuteAsync("INSERT INTO dbo.RcNoCount VALUES (1),(2),(3); INSERT INTO dbo.RcDeclare VALUES (1),(2),(3); INSERT INTO dbo.RcInit VALUES (1),(2),(3);");

        Assert.Equal(0, await ScalarAsync<int>("SELECT Seen FROM dbo.RcLog WHERE Prefix = 'set-nocount';"));
        Assert.Equal(3, await ScalarAsync<int>("SELECT Seen FROM dbo.RcLog WHERE Prefix = 'bare-declare';"));
        Assert.Equal(1, await ScalarAsync<int>("SELECT Seen FROM dbo.RcLog WHERE Prefix = 'init-declare';"));
    }

    [Fact]
    public async Task AggregateWrappedInAnExpression_OverAMultiRowInsert_IsDeterministic()
    {
        await ExecuteAsync("INSERT INTO dbo.AggT (Id, Val) VALUES (1, 10), (2, 30), (3, 20);");

        Assert.Equal(31, await ScalarAsync<int>("SELECT Captured FROM dbo.AggLog;"));
    }
}
