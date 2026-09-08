using Microsoft.Data.SqlClient;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
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
}
