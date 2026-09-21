using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed partial class ConcurrencyAndRuntimeEngineFactOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(ConcurrencyAndRuntimeEngineFactOracleTests);

    protected override string Ddl => """
        CREATE TABLE dbo.CsTarget (Id INT NOT NULL, V INT NOT NULL);
        CREATE CLUSTERED COLUMNSTORE INDEX CCI ON dbo.CsTarget;
        INSERT INTO dbo.CsTarget SELECT TOP (200000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)), 1 FROM sys.all_objects a, sys.all_objects b, sys.all_objects c;
        CREATE TABLE dbo.CsIndexed (Id INT NOT NULL, V INT NOT NULL);
        CREATE CLUSTERED COLUMNSTORE INDEX CCI ON dbo.CsIndexed;
        CREATE NONCLUSTERED INDEX IX_CsIndexed_Id ON dbo.CsIndexed(Id);
        INSERT INTO dbo.CsIndexed SELECT Id, V FROM dbo.CsTarget;
        CREATE TABLE dbo.RsTarget (Id INT NOT NULL PRIMARY KEY, V INT NOT NULL);
        INSERT INTO dbo.RsTarget SELECT Id, V FROM dbo.CsTarget;
        GO
        CREATE TABLE dbo.Dr (Id INT PRIMARY KEY, V INT NOT NULL);
        INSERT INTO dbo.Dr VALUES (1, 10), (2, 20);
        GO
        CREATE TABLE dbo.NoisyBase (Id INT);
        CREATE TABLE dbo.QuietBase (Id INT);
        CREATE TABLE dbo.NoisyAudit (Id INT);
        CREATE TABLE dbo.QuietAudit (Id INT);
        GO
        CREATE PROCEDURE dbo.NoisyProc AS
        BEGIN
            INSERT INTO dbo.NoisyAudit VALUES (1);
            INSERT INTO dbo.NoisyAudit VALUES (2);
        END
        GO
        CREATE PROCEDURE dbo.QuietProc AS
        BEGIN
            SET NOCOUNT ON;
            INSERT INTO dbo.QuietAudit VALUES (1);
            INSERT INTO dbo.QuietAudit VALUES (2);
        END
        GO
        CREATE TRIGGER dbo.trg_Noisy ON dbo.NoisyBase AFTER INSERT AS INSERT INTO dbo.NoisyAudit VALUES (1);
        GO
        CREATE TRIGGER dbo.trg_Quiet ON dbo.QuietBase AFTER INSERT AS BEGIN SET NOCOUNT ON; INSERT INTO dbo.QuietAudit VALUES (1); END
        GO
        CREATE TABLE dbo.RbLoop (Id INT PRIMARY KEY, V INT NOT NULL);
        CREATE TABLE dbo.RbSet (Id INT PRIMARY KEY, V INT NOT NULL);
        INSERT INTO dbo.RbLoop SELECT TOP (20) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)), 0 FROM sys.all_objects;
        INSERT INTO dbo.RbSet SELECT Id, V FROM dbo.RbLoop;
        GO
        CREATE PROCEDURE dbo.LoopProc AS
        BEGIN
            SET NOCOUNT ON;
            DECLARE @i INT = 1;
            WHILE @i <= 20
            BEGIN
                UPDATE dbo.RbLoop SET V = V + 1 WHERE Id = @i;
                SET @i += 1;
            END
        END
        GO
        CREATE PROCEDURE dbo.SetProc AS
        BEGIN
            SET NOCOUNT ON;
            UPDATE dbo.RbSet SET V = V + 1 WHERE Id BETWEEN 1 AND 20;
        END
        GO
        CREATE TABLE dbo.Cn (Id INT IDENTITY PRIMARY KEY, K INT NOT NULL, Pad CHAR(200) DEFAULT 'x');
        INSERT INTO dbo.Cn (K) SELECT TOP (50000) 1 FROM sys.all_objects a, sys.all_objects b, sys.all_objects c;
        CREATE INDEX IX_Cn ON dbo.Cn(K);
        GO
        """;

    private static async Task<int> StatementCompletedCountAsync(SqlConnection connection, string sql)
    {
        var count = 0;
        await using var command = new SqlCommand(sql, connection);
        command.StatementCompleted += (_, _) => count++;
        await command.ExecuteNonQueryAsync();
        return count;
    }

    private static async Task<long> LogicalReadsAsync(SqlConnection connection, string sql)
    {
        long reads = 0;
        void Handler(object _, SqlInfoMessageEventArgs e)
        {
            foreach (Match match in LogicalReadsRegex().Matches(e.Message))
            {
                reads += long.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        connection.InfoMessage += Handler;
        try
        {
            await ExecuteAsync(connection, "SET STATISTICS IO ON;");
            await ExecuteAsync(connection, sql);
            await ExecuteAsync(connection, "SET STATISTICS IO OFF;");
        }
        finally
        {
            connection.InfoMessage -= Handler;
        }

        return reads;
    }

    [GeneratedRegex(@"Table 'Cn'\..*?logical reads (\d+)")]
    private static partial Regex LogicalReadsRegex();

    [Fact]
    [Trait("Rule", "silentscan/index-design/columnstore-index-on-dml-target-table")]
    public async Task ColumnstoreRowgroupDelete_BlocksAnotherDeleteInSameRowgroup_RowstoreControlDoesNot()
    {
        Assert.True(await ScalarAsync<int>("SELECT COUNT(*) FROM sys.dm_db_column_store_row_group_physical_stats WHERE object_id = OBJECT_ID('dbo.CsTarget') AND state_desc = 'COMPRESSED';") > 0);

        await using var first = await OpenConnectionAsync();
        await using var second = await OpenConnectionAsync();
        await ExecuteAsync(first, "BEGIN TRAN; DELETE FROM dbo.CsTarget WHERE Id = 1; DELETE FROM dbo.CsIndexed WHERE Id = 1; DELETE FROM dbo.RsTarget WHERE Id = 1;");
        await ExecuteAsync(second, "SET LOCK_TIMEOUT 1000;");

        var columnstore = await SqlErrorNumberAsync(second, "DELETE FROM dbo.CsTarget WHERE Id = 2;");
        var indexedSeek = await SqlErrorNumberAsync(second, "DELETE FROM dbo.CsIndexed WHERE Id = 2;");
        var rowstore = await SqlErrorNumberAsync(second, "DELETE FROM dbo.RsTarget WHERE Id = 2;");
        await ExecuteAsync(first, "ROLLBACK;");

        Assert.Equal(1222, columnstore);
        Assert.Null(rowstore);
        Assert.Null(indexedSeek);
    }

    [Fact]
    [Trait("Rule", "silentscan/control-flow/dirty-read-isolation-hint")]
    public async Task NolockReaderSeesUncommittedValue_DefaultReaderIsBlocked()
    {
        await using var writer = await OpenConnectionAsync();
        await using var reader = await OpenConnectionAsync();
        await ExecuteAsync(writer, "BEGIN TRAN; UPDATE dbo.Dr SET V = 99 WHERE Id = 1;");
        await ExecuteAsync(reader, "SET LOCK_TIMEOUT 1000;");

        var dirty = await ScalarAsync<int>(reader, "SELECT V FROM dbo.Dr WITH (NOLOCK) WHERE Id = 1;");
        var blocked = await SqlErrorNumberAsync(reader, "SELECT V FROM dbo.Dr WHERE Id = 1;");
        await ExecuteAsync(writer, "ROLLBACK;");
        var committed = await ScalarAsync<int>(reader, "SELECT V FROM dbo.Dr WHERE Id = 1;");

        Assert.Equal(99, dirty);
        Assert.Equal(1222, blocked);
        Assert.Equal(10, committed);
    }

    [Fact]
    [Trait("Rule", "silentscan/statement-shape/missing-set-nocount-on")]
    public async Task WithoutNocount_EachInnerDmlReportsARowcountMessage_NocountControlDoesNot()
    {
        await using var connection = await OpenConnectionAsync();

        var noisyProc = await StatementCompletedCountAsync(connection, "EXEC dbo.NoisyProc;");
        var quietProc = await StatementCompletedCountAsync(connection, "EXEC dbo.QuietProc;");
        var noisyTrigger = await StatementCompletedCountAsync(connection, "INSERT INTO dbo.NoisyBase VALUES (1);");
        var quietTrigger = await StatementCompletedCountAsync(connection, "INSERT INTO dbo.QuietBase VALUES (1);");

        Assert.True(noisyProc > quietProc, $"{noisyProc} vs {quietProc}");
        Assert.True(noisyProc >= 2);
        Assert.True(noisyTrigger > quietTrigger, $"{noisyTrigger} vs {quietTrigger}");
    }

    [Fact]
    [Trait("Rule", "silentscan/query/rbar-single-row-loop-dml")]
    public async Task SingleRowLoopUpdateExecutesOncePerIteration_SetBasedUpdateExecutesOnce()
    {
        await ExecuteAsync("EXEC dbo.LoopProc; EXEC dbo.SetProc;");

        const string executions = "SELECT ISNULL(SUM(qs.execution_count), 0) FROM sys.dm_exec_query_stats qs CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) st CROSS APPLY (SELECT SUBSTRING(st.text, qs.statement_start_offset / 2 + 1, (CASE qs.statement_end_offset WHEN -1 THEN DATALENGTH(st.text) ELSE qs.statement_end_offset END - qs.statement_start_offset) / 2 + 1) AS stmt) s WHERE st.dbid = DB_ID() AND s.stmt LIKE 'UPDATE dbo.{0}%';";

        Assert.Equal(20L, await ScalarAsync<long>(executions.Replace("{0}", "RbLoop", StringComparison.Ordinal)));
        Assert.Equal(1L, await ScalarAsync<long>(executions.Replace("{0}", "RbSet", StringComparison.Ordinal)));
    }

    [Fact]
    [Trait("Rule", "silentscan/query/count-star-variable-existence-check")]
    public async Task CountStarIntoVariable_ReadsFullSet_ExistsAndInlineFormsShortCircuit()
    {
        await using var connection = await OpenConnectionAsync();

        var assign = await LogicalReadsAsync(connection, "DECLARE @c INT; SELECT @c = COUNT(*) FROM dbo.Cn WHERE K = 1; IF @c > 0 SELECT 1;");
        var exists = await LogicalReadsAsync(connection, "IF EXISTS (SELECT 1 FROM dbo.Cn WHERE K = 1) SELECT 1;");
        var inline = await LogicalReadsAsync(connection, "IF (SELECT COUNT(*) FROM dbo.Cn WHERE K = 1) > 0 SELECT 1;");

        Assert.True(exists > 0);
        Assert.True(assign > exists * 10, $"{assign} vs {exists}");
        Assert.Equal(exists, inline);
    }
}
