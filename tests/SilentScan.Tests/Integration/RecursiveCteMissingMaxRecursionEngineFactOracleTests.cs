using Microsoft.Data.SqlClient;
using SilentScan.Tests.Support;

namespace SilentScan.Tests.Integration;

[Trait("Category", "Oracle")]
public sealed class RecursiveCteMissingMaxRecursionEngineFactOracleTests : OracleTestFixture
{
    protected override string DatabaseNameSeed => nameof(RecursiveCteMissingMaxRecursionEngineFactOracleTests);

    protected override string Ddl => string.Empty;

    [Fact]
    public async Task RecursiveCteWithNoExplicitMaxRecursion_AbortsAtExactlyTheDefaultOf100()
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(DatabaseName));
        await connection.OpenAsync();

        await using var withinDefault = connection.CreateCommand();
        withinDefault.CommandText = """
            WITH R AS (SELECT 1 AS n UNION ALL SELECT n + 1 FROM R WHERE n < 101)
            SELECT COUNT(*) FROM R;
            """;
        var withinDefaultCount = (int)(await withinDefault.ExecuteScalarAsync())!;
        Assert.Equal(101, withinDefaultCount);

        await using var beyondDefault = connection.CreateCommand();
        beyondDefault.CommandText = """
            WITH R AS (SELECT 1 AS n UNION ALL SELECT n + 1 FROM R WHERE n < 102)
            SELECT COUNT(*) FROM R;
            """;
        var exception = await Assert.ThrowsAsync<SqlException>(() => beyondDefault.ExecuteScalarAsync());
        Assert.Equal(530, exception.Number);
    }

    [Fact]
    public async Task RecursiveCteWithExplicitMaxRecursion_CompletesPastTheDefault()
    {
        await using var connection = new SqlConnection(Options.BuildConnectionString(DatabaseName));
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            WITH R AS (SELECT 1 AS n UNION ALL SELECT n + 1 FROM R WHERE n < 500)
            SELECT COUNT(*) FROM R OPTION (MAXRECURSION 1000);
            """;

        var count = (int)(await command.ExecuteScalarAsync())!;
        Assert.Equal(500, count);
    }
}
