using Microsoft.Data.SqlClient;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Predicates;

[Trait("Category", "Oracle")]
[Trait("Rule", FindingRuleIds.TruncateSwallowedRuleId)]
public sealed class TruncateSwallowedOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(TruncateSwallowedOracleTests);

    protected override string Ddl => """
        CREATE TABLE dbo.Foo (Id INT NOT NULL PRIMARY KEY);
        GO
        CREATE TABLE dbo.Bar (Id INT NOT NULL, FooId INT NOT NULL REFERENCES dbo.Foo(Id));
        GO
        CREATE TABLE dbo.Marker (Who VARCHAR(20) NOT NULL);
        GO
        """;

    private async Task<List<string>> RunAndCaptureMarkersAsync(string batch)
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(DatabaseName));
        await connection.OpenAsync();

        await using (var truncateCommand = new SqlCommand("DELETE FROM dbo.Marker;", connection))
        {
            await truncateCommand.ExecuteNonQueryAsync();
        }

        await using (var runCommand = new SqlCommand(batch, connection))
        {
            await runCommand.ExecuteNonQueryAsync();
        }

        var markers = new List<string>();
        await using (var readCommand = new SqlCommand("SELECT Who FROM dbo.Marker ORDER BY Who;", connection))
        await using (var reader = await readCommand.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                markers.Add(reader.GetString(0));
            }
        }

        return markers;
    }

    [Fact]
    public async Task CatchThatExecutesARaisingProcedure_StillRaisesToTheCaller()
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(DatabaseName));
        await connection.OpenAsync();

        await using (var create = new SqlCommand("CREATE OR ALTER PROCEDURE dbo.RethrowTruncate AS THROW 50001, 'logged and rethrown', 1;", connection))
        {
            await create.ExecuteNonQueryAsync();
        }

        await using var run = new SqlCommand(
            "BEGIN TRY TRUNCATE TABLE dbo.Foo; END TRY BEGIN CATCH EXEC dbo.RethrowTruncate; END CATCH;",
            connection);
        var ex = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteNonQueryAsync());

        Assert.Equal(50001, ex.Number);
    }

    [Fact]
    public async Task NestedTruncateFailure_RoutesToNearestCatchOnly()
    {
        var markers = await RunAndCaptureMarkersAsync("""
            BEGIN TRY
                BEGIN TRY
                    TRUNCATE TABLE dbo.Foo;
                END TRY
                BEGIN CATCH
                    INSERT INTO dbo.Marker (Who) VALUES ('inner');
                END CATCH;
            END TRY
            BEGIN CATCH
                INSERT INTO dbo.Marker (Who) VALUES ('outer');
            END CATCH;
            """);

        Assert.Equal(["inner"], markers);
    }

    [Fact]
    public async Task NestedTruncateFailure_InnerRethrow_AlsoRunsOuterCatch()
    {
        var markers = await RunAndCaptureMarkersAsync("""
            BEGIN TRY
                BEGIN TRY
                    TRUNCATE TABLE dbo.Foo;
                END TRY
                BEGIN CATCH
                    INSERT INTO dbo.Marker (Who) VALUES ('inner');
                    THROW;
                END CATCH;
            END TRY
            BEGIN CATCH
                INSERT INTO dbo.Marker (Who) VALUES ('outer');
            END CATCH;
            """);

        Assert.Equal(["inner", "outer"], markers);
    }

    [Fact]
    public async Task TruncatingATempTable_NeverFails_WhileTruncatingAnFkReferencedTableDoes()
    {
        var markers = await RunAndCaptureMarkersAsync(
            """
            CREATE TABLE #t (Id INT);
            BEGIN TRY TRUNCATE TABLE #t; INSERT dbo.Marker VALUES ('temp-ok'); END TRY BEGIN CATCH INSERT dbo.Marker VALUES ('temp-caught'); END CATCH;
            BEGIN TRY TRUNCATE TABLE dbo.Foo; INSERT dbo.Marker VALUES ('fk-ok'); END TRY BEGIN CATCH INSERT dbo.Marker VALUES ('fk-caught'); END CATCH;
            """);

        Assert.Equal(["fk-caught", "temp-ok"], markers);
    }
}
